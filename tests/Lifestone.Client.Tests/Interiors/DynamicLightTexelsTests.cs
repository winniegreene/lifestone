using System.Numerics;
using Lifestone.Client.Interiors;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>M3 4.1 r2, the cell glow's table (<see cref="DynamicLightTexels"/>): the packing order, the cap, the units and the box test the cell counter uses. No dats needed.</summary>
public sealed class DynamicLightTexelsTests
{
    private static DynamicFrameLight Light(uint owner, float reach, Vector3 radiance) =>
        new(owner, 0x00000100, Vector3.Zero, radiance, reach / 1.5f, reach);

    [Fact]
    public void The_table_is_eight_lights_of_two_texels_in_one_row()
    {
        Assert.Equal(8, DynamicLightTexels.MaxLights);
        Assert.Equal(16, DynamicLightTexels.Width);
        Assert.Equal(64, DynamicLightTexels.Floats);
        Assert.True(DynamicLightList.MaxLights <= DynamicLightTexels.MaxLights);
    }

    [Fact]
    public void A_light_is_its_position_and_reach_then_its_radiance_in_the_order_given()
    {
        DynamicFrameLight[] lights = [Light(1, 15f, new Vector3(1f, 0.5f, 0.25f)), Light(2, 3f, new Vector3(2f, 2f, 2f))];
        Vector3[] positions = [new(10f, 20f, 30f), new(-1f, -2f, -3f)];
        var texels = new float[DynamicLightTexels.Floats];
        Array.Fill(texels, 9f); // stale contents are cleared

        var count = DynamicLightTexels.Pack(lights, positions, texels);

        Assert.Equal(2, count);
        Assert.Equal([10f, 20f, 30f, 15f, 1f, 0.5f, 0.25f, 0f], texels[..8]);
        Assert.Equal([-1f, -2f, -3f, 3f, 2f, 2f, 2f, 0f], texels[8..16]);
        Assert.All(texels[16..], t => Assert.Equal(0f, t)); // reach 0 ends the list in the shader
    }

    [Fact]
    public void The_reach_is_the_frames_reach_which_is_falloff_times_one_and_a_half_and_the_radiance_is_colour_times_intensity()
    {
        var list = new DynamicLightList { ViewerLightEnabled = false };
        Assert.True(list.Upsert(7, 0x00000100, new Vector3(1f, 2f, 3f), new Vector3(1f, 0.5f, 0.25f), 4f, 6f, on: true));
        list.Update(new Lifestone.Data.World.CellFrame(0x00000100, new Lifestone.Data.Animation.AcFrame(Vector3.Zero, Quaternion.Identity)), null);
        var frame = new DynamicLightFrame();
        frame.Fill(list);
        var texels = new float[DynamicLightTexels.Floats];

        var count = DynamicLightTexels.Pack(frame.Lights, [new Vector3(5f, 6f, 7f)], texels);

        Assert.Equal(1, count);
        Assert.Equal(9f, texels[3]); // 6 x 1.5
        Assert.Equal([4f, 2f, 1f], texels[4..7]); // (1, 0.5, 0.25) x 4
    }

    [Fact]
    public void More_lights_than_the_table_holds_keep_the_first_eight()
    {
        var lights = new DynamicFrameLight[10];
        var positions = new Vector3[10];
        for (var i = 0; i < lights.Length; i++)
        {
            lights[i] = Light((uint)i + 1, 3f + i, Vector3.One);
            positions[i] = new Vector3(i, 0f, 0f);
        }

        var texels = new float[DynamicLightTexels.Floats];
        var count = DynamicLightTexels.Pack(lights, positions, texels);

        Assert.Equal(8, count);
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(i, texels[i * 8]); // position x
            Assert.Equal(3f + i, texels[(i * 8) + 3]); // reach
        }
    }

    [Fact]
    public void A_light_with_no_reach_is_left_out_and_does_not_end_the_list()
    {
        DynamicFrameLight[] lights = [Light(1, 0f, Vector3.One), Light(2, 5f, Vector3.One), Light(3, float.NaN, Vector3.One), Light(4, 6f, Vector3.One)];
        Vector3[] positions = [new(1f, 0f, 0f), new(2f, 0f, 0f), new(3f, 0f, 0f), new(4f, 0f, 0f)];
        var texels = new float[DynamicLightTexels.Floats];

        var count = DynamicLightTexels.Pack(lights, positions, texels);

        Assert.Equal(2, count);
        Assert.Equal(2f, texels[0]);
        Assert.Equal(4f, texels[8]);
    }

    [Fact]
    public void An_empty_frame_clears_the_table_and_a_short_buffer_or_position_list_is_refused()
    {
        var texels = new float[DynamicLightTexels.Floats];
        Array.Fill(texels, 1f);
        Assert.Equal(0, DynamicLightTexels.Pack([], [], texels));
        Assert.All(texels, t => Assert.Equal(0f, t));
        Assert.Throws<ArgumentException>(() => DynamicLightTexels.Pack([], [], new float[DynamicLightTexels.Floats - 1]));
        Assert.Throws<ArgumentException>(() => DynamicLightTexels.Pack([Light(1, 3f, Vector3.One)], [], texels));
    }

    [Fact]
    public void A_light_reaches_a_box_when_its_nearest_point_is_within_the_reach()
    {
        var min = new Vector3(-1f, -1f, -1f);
        var max = new Vector3(1f, 1f, 1f);
        Assert.True(DynamicLightTexels.ReachesBox(Vector3.Zero, 0.1f, min, max)); // inside
        Assert.True(DynamicLightTexels.ReachesBox(new Vector3(4f, 0f, 0f), 3f, min, max)); // 3 m from the face: exactly the reach
        Assert.False(DynamicLightTexels.ReachesBox(new Vector3(4.01f, 0f, 0f), 3f, min, max));
        Assert.True(DynamicLightTexels.ReachesBox(new Vector3(2f, 2f, 0f), 1.5f, min, max)); // past a corner edge: sqrt(2) = 1.41
        Assert.False(DynamicLightTexels.ReachesBox(new Vector3(2f, 2f, 2f), 1.5f, min, max)); // past a corner: sqrt(3) = 1.73
    }
}
