using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>
/// M3 4.1 r1, what the row writers hand the light table: the merged set of <see cref="ObjectLightSets.Select(Vector3, float, uint, IReadOnlyList{CellLight}, DynamicLightFrame, Span{ObjectLight})"/> for a
/// player standing in a cell with a viewer light and a passing bolt (<c>Render::minimize_object_lighting</c> 11.6096 0x0054E090: the dynamic lights first, then the static ones, 8 at most, each by its own reach).
/// No dats needed.
/// </summary>
public sealed class ObjectLightRowTests
{
    private const uint Cell = 0xA9B40019;
    private static readonly Vector3 Orange = new(1f, 0.5f, 0.25f);
    private static readonly Vector3 White = new(1f, 1f, 1f);

    private static CellFrame At(float x, float y, float z = 0f) => new(Cell, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    private static CellLight Static(float x, float falloff = 10f) =>
        new(0x00070143, 0x02000000, new Vector3(x, 0f, 0f), new Vector3(x, 0f, 0f), new Vector3(0f, 1f, 0f), 100f, falloff);

    private static DynamicLightFrame FrameOf(bool viewer, params float[] boltX)
    {
        var list = new DynamicLightList { ViewerLightEnabled = viewer };
        for (var i = 0; i < boltX.Length; i++)
        {
            Assert.True(list.Upsert((uint)(i + 1), Cell, new Vector3(boltX[i], 0f, 1f), Orange, 4f, 6f, true));
        }

        list.Update(At(0f, 0f), At(0f, 0f));
        var frame = new DynamicLightFrame();
        frame.Fill(list);
        return frame;
    }

    [Fact]
    public void A_player_in_a_cell_with_a_bolt_and_the_viewer_light_gets_both_ahead_of_the_static_light()
    {
        var frame = FrameOf(true, 3f);
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(new Vector3(0f, 0f, 0.9f), 0.5f, Cell, [Static(1f)], frame, set);
        Assert.Equal(3, count);
        Assert.Equal(White * DynamicLightList.ViewerIntensity, set[0].Radiance); // the list is nearest the viewer first: the viewer light (2 m up) leads the bolt (3 m away)
        Assert.Equal(Orange * 4f, set[1].Radiance);
        Assert.Equal(new Vector3(0f, 100f, 0f), set[2].Radiance); // the static light is last
    }

    [Fact]
    public void The_viewer_light_alone_is_one_white_entry_two_metres_above_the_player_and_its_reach_is_one_and_a_half_falloffs()
    {
        var frame = FrameOf(true);
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(new Vector3(0f, 0f, 0.9f), 0.5f, Cell, [], frame, set);
        Assert.Equal(1, count);
        Assert.Equal(White * DynamicLightList.ViewerIntensity, set[0].Radiance);
        Assert.Equal(new Vector3(0f, 0f, 2f), set[0].Position);
        Assert.Equal(DynamicLightList.ViewerFalloff * 1.5f, set[0].Reach);
    }

    [Fact]
    public void The_row_never_holds_more_than_eight_lights_and_the_dynamic_ones_push_the_static_ones_out()
    {
        var frame = FrameOf(true, 1f, 2f, 3f, 4f, 5f, 6f, 7f);
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(new Vector3(0f, 0f, 0.9f), 0.5f, Cell, [Static(1f), Static(2f), Static(3f)], frame, set);
        Assert.Equal(ObjectLightSets.MaxLights, count);
        var statics = 0;
        for (var i = 0; i < count; i++)
        {
            statics += set[i].Radiance == new Vector3(0f, 100f, 0f) ? 1 : 0;
        }

        Assert.Equal(1, statics); // seven dynamic (the list's cap), one static
        Assert.Equal(new Vector3(0f, 100f, 0f), set[7].Radiance);
    }

    [Fact]
    public void A_bolt_beyond_falloff_plus_the_players_radius_is_not_in_the_row_and_one_just_inside_is()
    {
        var frame = FrameOf(false, 4.4f, 4.6f); // falloff 6 + radius 0.5 around a centre 2 m below the light height... measured from the centre (0,0,0.9)
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(new Vector3(0f, 0f, 0.9f), 0.5f, Cell, [], frame, set);
        Assert.Equal(2, count); // both are within 6.5 m
        count = ObjectLightSets.Select(new Vector3(0f, 0f, 0.9f), 0.5f, Cell, [], FrameOf(false, 6.4f, 6.6f), set);
        Assert.Equal(1, count); // 6.6 m (and 0.1 m up) is past 6.5
    }

    [Fact]
    public void With_the_bolt_gone_and_the_viewer_light_off_the_row_is_the_static_lights_alone()
    {
        var frame = FrameOf(false);
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(new Vector3(0f, 0f, 0.9f), 0.5f, Cell, [Static(1f)], frame, set);
        Assert.Equal(1, count);
        Assert.Equal(new Vector3(0f, 100f, 0f), set[0].Radiance);
    }
}
