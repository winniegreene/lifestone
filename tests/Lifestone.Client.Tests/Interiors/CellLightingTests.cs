using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Data.Tests;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>
/// Retail EnvCell static lighting (<c>calc_point_light</c>, <c>Render::insert_light</c>) and the hardware point lights
/// of objects in cells (<c>PrimD3DRender::config_hardware_light</c>, <c>Render::minimize_object_lighting</c>).
/// </summary>
public sealed class CellLightingTests(InteriorsFixture fixture) : IClassFixture<InteriorsFixture>
{
    private static CellLight Light(Vector3 position, Vector3 color, float intensity, float falloff) =>
        new(0x00070100, 0x02000001, position, position, color, intensity, falloff);

    private static Vector3 Burn1(Vector3 vertex, Vector3 normal, CellLight light)
    {
        float r = 0f, g = 0f, b = 0f;
        CellStaticLighting.AddPointLight(vertex, normal, light.Position, light, ref r, ref g, ref b);
        return new Vector3(r, g, b);
    }

    [Fact]
    public void Point_light_beyond_one_metre_falls_off_with_the_cube_of_the_distance_times_the_wrapped_cosine()
    {
        // d = (0, 0, 2): s = (0.5 × 2 + 2) / 1.5 = 2; k = s / (d² · d) = 0.25; × (1 - 2 / 13) × intensity 1.
        var c = Burn1(Vector3.Zero, Vector3.UnitZ, Light(new Vector3(0f, 0f, 2f), new Vector3(1f, 0.5f, 0.25f), 1f, 10f));
        var k = 0.25f * (1f - (2f / 13f));
        Assert.Equal(k, c.X, 5);
        Assert.Equal(k * 0.5f, c.Y, 5);
        Assert.Equal(k * 0.25f, c.Z, 5);
    }

    [Fact]
    public void Point_light_within_one_metre_divides_by_the_distance_only()
    {
        // d = 0.5: s = (0.25 + 0.5) / 1.5 = 0.5; k = s / d = 1; × (1 - 0.5 / 13).
        var c = Burn1(Vector3.Zero, Vector3.UnitZ, Light(new Vector3(0f, 0f, 0.5f), new Vector3(1f, 0.5f, 0.25f), 1f, 10f));
        var k = 1f - (0.5f / 13f);
        Assert.Equal(k, c.X, 5);
        Assert.Equal(k * 0.5f, c.Y, 5); // min(k × 0.5, 0.5)
        Assert.Equal(k * 0.25f, c.Z, 5);
    }

    [Fact]
    public void Point_light_reaches_falloff_times_one_point_three()
    {
        var color = Vector3.One;
        Assert.NotEqual(Vector3.Zero, Burn1(Vector3.Zero, Vector3.UnitZ, Light(new Vector3(0f, 0f, 12.9f), color, 100f, 10f)));
        Assert.Equal(Vector3.Zero, Burn1(Vector3.Zero, Vector3.UnitZ, Light(new Vector3(0f, 0f, 13.1f), color, 100f, 10f)));
    }

    [Fact]
    public void Point_light_wraps_past_ninety_degrees_but_not_behind()
    {
        var color = Vector3.One;
        // Beside the surface: n·d = 0, s = 0.5 d / 1.5 > 0.
        Assert.True(Burn1(Vector3.Zero, Vector3.UnitZ, Light(new Vector3(2f, 0f, 0f), color, 1f, 10f)).X > 0f);
        // Behind: s = (0.5 × 2 - 2) / 1.5 < 0.
        Assert.Equal(Vector3.Zero, Burn1(Vector3.Zero, Vector3.UnitZ, Light(new Vector3(0f, 0f, -2f), color, 1f, 10f)));
    }

    [Fact]
    public void Each_light_saturates_at_its_own_colour()
    {
        var c = Burn1(Vector3.Zero, Vector3.UnitZ, Light(new Vector3(0f, 0f, 2f), new Vector3(0.98f, 0.84f, 0.61f), 100f, 7f));
        Assert.Equal(new Vector3(0.98f, 0.84f, 0.61f), c);
    }

    [Theory]
    [InlineData(-0.1f, 0)]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 127)] // 127.5 truncated, as (unsigned __int8)(r × 255.0)
    [InlineData(1f, 255)]
    public void Vertex_colours_are_clamped_and_truncated_to_bytes(float value, byte expected) =>
        Assert.Equal(expected, CellStaticLighting.ToByte(value));

    [Fact]
    public void Burn_moves_the_lights_into_the_cell_frame()
    {
        // A cell at (10, 0, 0) turned 90° about Z; the light 2 m above its local origin.
        var frame = new Placement(new Vector3(10f, 0f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f));
        var light = Light(new Vector3(10f, 0f, 2f), new Vector3(1f, 0.5f, 0.25f), 1f, 10f);
        var rgb = new byte[6];
        CellStaticLighting.Burn([Vector3.Zero, new Vector3(0f, 20f, 0f)], [Vector3.UnitZ, Vector3.UnitZ], frame, [light], rgb);
        var k = 0.25f * (1f - (2f / 13f));
        Assert.Equal(CellStaticLighting.ToByte(k), rgb[0]);
        Assert.Equal(CellStaticLighting.ToByte(k * 0.5f), rgb[1]);
        Assert.Equal(new byte[] { 0, 0, 0 }, rgb[3..]); // 20 m away
    }

    [Fact]
    public void Hardware_light_is_colour_times_intensity_times_cosine_over_distance()
    {
        // PrimD3DRender::config_hardware_light: diffuse = colour × intensity, attenuation (0, 1, 0), range = falloff × 1.5.
        var light = Light(new Vector3(2f, 0f, 0f), new Vector3(0.5f, 0.25f, 0.1f), 1f, 2f);
        var rgb = new Vector3[1];
        Assert.True(CellObjectLighting.Light([Vector3.Zero], [Vector3.UnitX], [light], rgb));
        Assert.Equal(new Vector3(0.25f, 0.125f, 0.05f), rgb[0]);

        // 45° off the normal.
        Assert.True(CellObjectLighting.Light([Vector3.Zero], [Vector3.Normalize(new Vector3(1f, 1f, 0f))], [light], rgb));
        Assert.Equal(0.25f * MathF.Sqrt(0.5f), rgb[0].X, 5);
    }

    [Fact]
    public void Hardware_light_has_a_range_faces_and_a_clamp()
    {
        var rgb = new Vector3[1];
        var near = Light(new Vector3(2.9f, 0f, 0f), Vector3.One, 100f, 2f); // range 3
        var far = Light(new Vector3(3.1f, 0f, 0f), Vector3.One, 100f, 2f);
        Assert.True(CellObjectLighting.Light([Vector3.Zero], [Vector3.UnitX], [near], rgb));
        Assert.Equal(Vector3.One, rgb[0]); // 100 / 2.9 clamps to 1
        Assert.False(CellObjectLighting.Light([Vector3.Zero], [Vector3.UnitX], [far], rgb));
        Assert.False(CellObjectLighting.Light([Vector3.Zero], [-Vector3.UnitX], [near], rgb)); // facing away
        Assert.False(CellObjectLighting.Light([Vector3.Zero], [Vector3.Zero], [near], rgb)); // zero normal: N·L = 0
    }

    [Fact]
    public void At_most_eight_hardware_lights_that_reach_the_sphere_are_enabled_in_list_order()
    {
        var lights = Enumerable.Range(0, 12)
            .Select(i => Light(new Vector3(i, 0f, 0f), Vector3.One, 1f, 1f))
            .ToList();
        // Sphere at the origin with radius 2: lights up to 1 + 2 = 3 m away reach it (4 lights).
        var picked = CellObjectLighting.SelectFor(Vector3.Zero, 2f, lights);
        Assert.Equal([0f, 1f, 2f, 3f], picked.Select(l => l.Position.X));
        var many = CellObjectLighting.SelectFor(Vector3.Zero, 100f, lights);
        Assert.Equal(CellObjectLighting.MaxLights, many.Count);
        Assert.Equal(lights.Take(8), many);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Cell_lights_come_from_the_static_objects_setups_and_are_all_point_lights()
    {
        var coord = InteriorLocations.TownNetwork.Location.Position.Landblock;
        var lights = fixture.CellLights(coord);
        var all = lights.Values.SelectMany(l => l).ToList();
        Assert.Equal(171, all.Count); // measured: 171 static objects with a light in the Town Network
        Assert.All(all, l =>
        {
            Assert.InRange(l.Falloff, 1f, 100f);
            Assert.InRange(l.Intensity, 20f, 100f);
            Assert.InRange(l.Color.X, 0f, 1f);
        });

        // The light sits at object frame ∘ light offset.
        var cell = fixture.Block(coord).Cells.Cells.First(c => lights[c.Id].Count > 0);
        var light = lights[cell.Id][0];
        var obj = cell.StaticObjects.First(o => o.ModelId == light.ModelId);
        var offset = fixture.LightsOfModel(obj.ModelId)[0].Offset.Origin;
        Assert.Equal(obj.Frame.LocalToParent(offset), light.Position);
        Assert.Equal(obj.Frame.Origin, light.ObjectOrigin);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void A_viewer_cell_gets_the_nearest_forty_lights_of_its_stab_list()
    {
        var coord = InteriorLocations.TownNetwork.Location.Position.Landblock;
        var lights = fixture.CellLights(coord);
        var viewer = fixture.Block(coord).Cells.GetCell(InteriorLocations.TownNetwork.CellId)!;
        var selected = CellStaticLighting.SelectFor(viewer, id => lights.TryGetValue(id, out var l) ? l : []);
        var candidates = PortalVisibility.PotentiallyVisibleSet(viewer).SelectMany(id => lights.TryGetValue(id, out var l) ? l : []).ToList();
        Assert.True(candidates.Count > CellStaticLighting.MaxStaticLights);
        Assert.Equal(CellStaticLighting.MaxStaticLights, selected.Count);

        var distances = selected.Select(l => Vector3.DistanceSquared(l.ObjectOrigin, viewer.Frame.Origin)).ToList();
        Assert.Equal(distances.Order(), distances);
        var cutoff = distances[^1];
        Assert.All(candidates.Except(selected), l => Assert.True(Vector3.DistanceSquared(l.ObjectOrigin, viewer.Frame.Origin) >= cutoff));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Burned_cells_near_lights_are_lit_and_dungeons_have_unlit_corners()
    {
        // The Town Network hub: its corners near the wall lights saturate, the far ones stay black.
        var block = fixture.Block(InteriorLocations.TownNetwork.Location.Position.Landblock);
        var lights = fixture.CellLights(block.Coord);
        var cell = block.Cells.GetCell(InteriorLocations.TownNetwork.CellId)!;
        var meshes = new CellMeshCache(fixture.Structures);
        var selected = CellStaticLighting.SelectFor(cell, id => lights.TryGetValue(id, out var l) ? l : []);
        var bytes = new List<byte>();
        foreach (var sub in meshes.Get(cell).Mesh.SubMeshes)
        {
            var rgb = new byte[sub.Positions.Length * 3];
            CellStaticLighting.Burn(sub.Positions, sub.Normals, cell.Frame, selected, rgb);
            bytes.AddRange(rgb);
        }

        Assert.Contains((byte)255, bytes);
        Assert.Contains((byte)0, bytes);
    }
}
