using System.Numerics;
using Lifestone.Client.Interiors;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>The light set of a moving object in a cell (D46, <c>Render::minimize_object_lighting</c>): at most eight lights, those that reach its sphere.</summary>
public sealed class ObjectLightSetsTests
{
    private static CellLight Light(float x, float y, float z, float falloff = 4f, float intensity = 100f) =>
        new(0x00070143, 0x02000000, new Vector3(x, y, z), new Vector3(x, y, z), new Vector3(1f, 0.5f, 0.25f), intensity, falloff);

    [Fact]
    public void A_light_that_reaches_the_sphere_is_kept_with_its_radiance_and_reach()
    {
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(new Vector3(70f, -60f, 1f), 1f, [Light(72f, -57f, 2f, falloff: 4f, intensity: 100f)], set);
        Assert.Equal(1, count);
        Assert.Equal(new Vector3(72f, -57f, 2f), set[0].Position);
        Assert.Equal(new Vector3(100f, 50f, 25f), set[0].Radiance);
        Assert.Equal(4f * CellObjectLighting.RangeAdjust, set[0].Reach);
    }

    [Fact]
    public void A_light_whose_falloff_does_not_touch_the_sphere_is_left_out()
    {
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        // 10 m away, falloff 4 plus the sphere's 1 m: out of reach.
        Assert.Equal(0, ObjectLightSets.Select(Vector3.Zero, 1f, [Light(10f, 0f, 0f)], set));
        // Exactly at falloff + radius: kept (retail keeps a light when the distance is at most that sum).
        Assert.Equal(1, ObjectLightSets.Select(Vector3.Zero, 1f, [Light(5f, 0f, 0f)], set));
    }

    [Fact]
    public void At_most_eight_lights_are_kept_in_the_order_of_the_candidates()
    {
        var candidates = Enumerable.Range(0, 20).Select(i => Light(i * 0.1f, 0f, 0f)).ToArray();
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(Vector3.Zero, 1f, candidates, set);
        Assert.Equal(ObjectLightSets.MaxLights, count);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(i * 0.1f, set[i].Position.X);
        }
    }

    [Fact]
    public void The_selection_is_the_one_statics_get()
    {
        var candidates = Enumerable.Range(0, 30).Select(i => Light((i % 7) * 1.5f, (i / 7) * 2f, 1f, falloff: 2f)).ToArray();
        var center = new Vector3(3f, 2f, 1f);
        var expected = CellObjectLighting.SelectFor(center, 1f, candidates);
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(center, 1f, candidates, set);
        Assert.Equal(expected.Count, count);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(expected[i].Position, set[i].Position);
        }
    }

    [Fact]
    public void A_set_too_small_to_hold_eight_lights_is_refused()
    {
        var small = new ObjectLight[ObjectLightSets.MaxLights - 1];
        Assert.Throws<ArgumentException>(() => ObjectLightSets.Select(Vector3.Zero, 1f, [], small));
    }

    [Fact]
    public void A_row_of_the_table_is_two_texels_for_each_of_eight_lights()
    {
        Assert.Equal(16, ObjectLightSets.RowWidth);
        Assert.Equal(2, ObjectLightSets.TexelsPerLight);
    }

    [Fact]
    public void Selecting_allocates_nothing()
    {
        var candidates = Enumerable.Range(0, 40).Select(i => Light(i * 0.2f, 0f, 0f)).ToArray();
        var set = new ObjectLight[ObjectLightSets.MaxLights];
        ObjectLightSets.Select(Vector3.Zero, 1f, candidates, set);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            ObjectLightSets.Select(Vector3.Zero, 1f, candidates, set);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
