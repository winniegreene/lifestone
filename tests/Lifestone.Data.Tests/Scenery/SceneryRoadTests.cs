using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Data.Tests.Scenery;

/// <summary>
/// CLandBlock::on_road over all 16 road-corner patterns of one cell, with expectations written from the geometry
/// (a 5-unit band along each road edge / around each road corner), not from the implementation.
/// </summary>
public sealed class SceneryRoadTests
{
    /// <summary>A landblock whose cell (2, 3) has road bits on the given corners (SW, NW, SE, NE).</summary>
    private static LandblockTerrain Cell(bool sw, bool nw, bool se, bool ne)
    {
        var words = new ushort[81];
        words[TerrainConstants.VertexIndex(2, 3)] = (ushort)(sw ? 1 : 0);
        words[TerrainConstants.VertexIndex(2, 4)] = (ushort)(nw ? 2 : 0);
        words[TerrainConstants.VertexIndex(3, 3)] = (ushort)(se ? 3 : 0);
        words[TerrainConstants.VertexIndex(3, 4)] = (ushort)(ne ? 1 : 0);
        return new LandblockTerrain(new LandblockCoord(0x10, 0x20), words, new byte[81]);
    }

    private static bool OnRoad(LandblockTerrain terrain, float dx, float dy) => SceneryRoad.OnRoad(terrain, 48f + dx, 72f + dy);

    [Fact]
    public void No_road_corners_is_never_on_road()
    {
        var terrain = Cell(false, false, false, false);
        for (var dx = 0f; dx < 24f; dx += 0.5f)
        {
            for (var dy = 0f; dy < 24f; dy += 0.5f)
            {
                Assert.False(OnRoad(terrain, dx, dy));
            }
        }
    }

    [Fact]
    public void All_four_road_corners_is_always_on_road()
    {
        var terrain = Cell(true, true, true, true);
        Assert.True(OnRoad(terrain, 12f, 12f));
        Assert.True(OnRoad(terrain, 0.1f, 23.9f));
    }

    [Theory]
    // Single corners: a triangle of legs 5 at that corner.
    [InlineData(true, false, false, false, 2f, 2.9f, true)]
    [InlineData(true, false, false, false, 2.6f, 2.6f, false)]
    [InlineData(false, true, false, false, 2f, 21.1f, true)]
    [InlineData(false, true, false, false, 2f, 20f, false)]
    [InlineData(false, false, true, false, 21.1f, 2f, true)]
    [InlineData(false, false, true, false, 20f, 2f, false)]
    [InlineData(false, false, false, true, 21.6f, 21.6f, true)]
    [InlineData(false, false, false, true, 21.4f, 21.4f, false)]
    // Two corners on one edge: a 5-unit band along that edge.
    [InlineData(true, true, false, false, 4.9f, 12f, true)]
    [InlineData(true, true, false, false, 5.1f, 12f, false)]
    [InlineData(true, false, true, false, 12f, 4.9f, true)]
    [InlineData(true, false, true, false, 12f, 5.1f, false)]
    [InlineData(false, true, false, true, 12f, 19.1f, true)]
    [InlineData(false, true, false, true, 12f, 18.9f, false)]
    [InlineData(false, false, true, true, 19.1f, 12f, true)]
    [InlineData(false, false, true, true, 18.9f, 12f, false)]
    // Opposite corners: a band of half-width 5 (in |dx -+ dy|) along that diagonal.
    [InlineData(true, false, false, true, 12f, 16.9f, true)]
    [InlineData(true, false, false, true, 12f, 17.1f, false)]
    [InlineData(false, true, true, false, 12f, 7.1f, true)]
    [InlineData(false, true, true, false, 12f, 6.9f, false)]
    // Three corners: the union of the two edge bands.
    [InlineData(true, true, true, false, 12f, 4.9f, true)]
    [InlineData(true, true, true, false, 4.9f, 23f, true)]
    [InlineData(true, true, true, false, 12f, 12f, false)]
    [InlineData(true, true, false, true, 12f, 19.1f, true)]
    [InlineData(true, true, false, true, 12f, 12f, false)]
    [InlineData(true, false, true, true, 19.1f, 12f, true)]
    [InlineData(true, false, true, true, 12f, 12f, false)]
    [InlineData(false, true, true, true, 19.1f, 2f, true)]
    [InlineData(false, true, true, true, 2f, 19.1f, true)]
    [InlineData(false, true, true, true, 12f, 12f, false)]
    public void Road_corner_patterns(bool sw, bool nw, bool se, bool ne, float dx, float dy, bool expected)
    {
        Assert.Equal(expected, OnRoad(Cell(sw, nw, se, ne), dx, dy));
    }

    [Fact]
    public void Band_edges_are_strict_as_in_retail()
    {
        // on_road compares dx < 5 and dy > 19 strictly: exactly 5.0 / 19.0 is off the road.
        Assert.False(OnRoad(Cell(true, true, false, false), 5f, 12f));
        Assert.True(OnRoad(Cell(true, true, false, false), 4.99999f, 12f));
        Assert.False(OnRoad(Cell(false, true, false, true), 12f, 19f));
        Assert.False(OnRoad(Cell(true, false, false, false), 2.5f, 2.5f));
    }
}
