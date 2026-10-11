using Lifestone.Client.Objects;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Tests.Objects;

/// <summary>The object grid's centre for a streaming focus (objects-active-in-dungeon-landblock).</summary>
public sealed class ObjectRingsCenterTests
{
    [Fact]
    public void A_dungeon_focus_has_no_object_grid_and_an_outdoor_focus_centres_it_on_its_landblock()
    {
        var holtburg = Lifestone.Client.World.AcCoordinates.LandblockOrigin(LandblockCoord.Holtburg);
        var inside = new Lifestone.Client.World.GlobalPosition(holtburg.X + 100, holtburg.Y + 50, holtburg.Z + 10);
        Assert.Equal(LandblockCoord.Holtburg, ObjectRings.CenterFor(new Lifestone.Client.Streaming.StreamingFocus(inside)));
        Assert.Null(ObjectRings.CenterFor(new Lifestone.Client.Streaming.StreamingFocus(inside, Lifestone.Client.Streaming.StreamingFocusKind.Indoor)));
    }
}

public sealed class ObjectRingsTests
{
    [Theory]
    [InlineData(0, 1, 8)]
    [InlineData(1, 1, 8)]
    [InlineData(2, 2, 4)]
    [InlineData(3, 4, 2)]
    [InlineData(4, 4, 2)]
    [InlineData(5, 8, 1)]
    [InlineData(8, 8, 1)]
    public void Polygon_size_and_side_cell_count_follow_get_block_orient(int ring, int polygonSize, int sideCellCount)
    {
        // LScape::get_block_orient thresholds (rings 0-1, 2, 3-4, beyond) and CLandBlockStruct::generate's 8 / poly_size.
        Assert.Equal(polygonSize, ObjectRings.RetailPolygonSize(ring));
        Assert.Equal(sideCellCount, ObjectRings.RetailSideCellCount(ring));
    }

    [Fact]
    public void Only_the_three_by_three_around_the_viewer_has_objects()
    {
        var center = LandblockCoord.Holtburg;
        var withObjects = 0;
        for (var dx = -3; dx <= 3; dx++)
        {
            for (var dy = -3; dy <= 3; dy++)
            {
                var coord = center.Offset(dx, dy)!.Value;
                var has = ObjectRings.HasObjects(coord, center);

                // init_static_objs / init_buildings run only at side_cell_count == 8.
                Assert.Equal(ObjectRings.RetailSideCellCount(ObjectRings.Ring(coord, center)) == 8, has);
                withObjects += has ? 1 : 0;
            }
        }

        Assert.Equal(9, withObjects);
        Assert.Equal(25, Enumerable.Range(-3, 7).SelectMany(dx => Enumerable.Range(-3, 7).Select(dy => center.Offset(dx, dy)!.Value))
            .Count(c => ObjectRings.HasObjects(c, center, radius: 2)));
    }
}
