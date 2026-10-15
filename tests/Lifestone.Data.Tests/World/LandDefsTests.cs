using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.World;

/// <summary>
/// <see cref="LandDefs"/> (retail <c>LandDefs</c>, moved from the Client's AcCoordinates in M2): the cell predicates,
/// the lcoord functions and <c>adjust_to_outside</c>, worked by hand at landblock edges and at the map edges 0x00 and
/// 0xFE (0xFF is off the map: lcoords stop below 2040 = 255 x 8).
/// </summary>
public sealed class LandDefsTests
{
    [Theory]
    [InlineData(0xA9B40001u, true, false, true)]
    [InlineData(0xA9B40040u, true, false, true)] // the 64th outdoor cell
    [InlineData(0xA9B40041u, false, false, false)] // past the outdoor cells, below the EnvCells
    [InlineData(0xA9B40000u, false, false, false)]
    [InlineData(0xA9B40100u, false, true, true)] // the first EnvCell
    [InlineData(0xA9B4FFFDu, false, true, true)] // the last EnvCell
    [InlineData(0xA9B4FFFEu, false, false, false)] // the LandBlockInfo record, not a cell
    [InlineData(0xA9B4FFFFu, false, false, true)] // the landblock itself
    public void Cell_predicates_split_outdoor_cells_EnvCells_and_the_landblock_id(uint cellId, bool outdoor, bool envCell, bool valid)
    {
        Assert.Equal(outdoor, LandDefs.IsOutdoorCell(cellId));
        Assert.Equal(envCell, LandDefs.IsEnvCell(cellId));
        Assert.Equal(valid, LandDefs.IsValidCellId(cellId));
    }

    [Theory]
    [InlineData(0x00, 0x00, true)]
    [InlineData(0xFE, 0xFE, true)]
    [InlineData(0xFF, 0x00, false)]
    [InlineData(0x00, 0xFF, false)]
    public void Landblocks_0x00_to_0xFE_are_on_the_map(byte x, byte y, bool onMap)
    {
        Assert.Equal(onMap, LandDefs.IsOnMap(new LandblockCoord(x, y)));
    }

    [Theory]
    [InlineData(0x00000001u, 0, 0)] // the south-west corner of the map
    [InlineData(0x00000040u, 7, 7)]
    [InlineData(0xFE000039u, 2039, 0)] // the south-east corner: cell (7, 0) of 0xFE00
    [InlineData(0x00FE0008u, 0, 2039)] // the north-west corner: cell (0, 7) of 0x00FE
    [InlineData(0xFEFE0040u, 2039, 2039)] // the north-east corner
    [InlineData(0xA9B40019u, 1355, 1440)] // Holtburg's town cell
    public void Lcoords_round_trip_up_to_the_map_edges(uint cellId, int x, int y)
    {
        Assert.True(LandDefs.TryGetLandCoord(cellId, out var lx, out var ly));
        Assert.Equal((x, y), (lx, ly));
        Assert.Equal(cellId, LandDefs.LandCoordToCellId(x, y));
    }

    [Theory]
    [InlineData(0xFF000001u)] // landblock 0xFF is off the map
    [InlineData(0x00FF0001u)]
    [InlineData(0xA9B40100u)] // an EnvCell has no lcoord
    [InlineData(0xA9B4FFFFu)]
    public void Only_on_map_outdoor_cells_have_an_lcoord(uint cellId)
    {
        Assert.False(LandDefs.TryGetLandCoord(cellId, out _, out _));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2040, 0)]
    [InlineData(0, 2040)]
    public void Lcoords_off_the_map_have_no_cell_id(int x, int y)
    {
        Assert.Equal(0u, LandDefs.LandCoordToCellId(x, y));
    }

    [Fact]
    public void Adjust_to_outside_crosses_a_landblock_edge_and_wraps_the_origin()
    {
        // Eastwards out of 0xA9B4: lcoord x 1352 + floor(192.5 / 24) = 1360 is cell (0, 0) of 0xAAB4.
        var cell = 0xA9B40001u;
        var local = new Vector3(192.5f, 7f, 3f);
        Assert.True(LandDefs.AdjustToOutside(ref cell, ref local));
        Assert.Equal(0xAAB40001u, cell);
        Assert.Equal(new Vector3(0.5f, 7f, 3f), local);

        // Westwards: lcoord x 1352 - 1 = 1351 is cell (7, 0) of 0xA8B4 (low word 7 * 8 + 0 + 1 = 0x39).
        cell = 0xA9B40001u;
        local = new Vector3(-0.5f, 7f, 3f);
        Assert.True(LandDefs.AdjustToOutside(ref cell, ref local));
        Assert.Equal(0xA8B40039u, cell);
        Assert.Equal(new Vector3(191.5f, 7f, 3f), local);
    }

    [Fact]
    public void Adjust_to_outside_takes_an_EnvCell_or_the_landblock_id_to_the_outdoor_cell_below()
    {
        // Holtburg's drop point given in its landblock: cell (3, 0) of 0xA9B4 is 0x19.
        var cell = 0xA9B4FFFFu;
        var local = new Vector3(84f, 7.1f, 94f);
        Assert.True(LandDefs.AdjustToOutside(ref cell, ref local));
        Assert.Equal(0xA9B40019u, cell);
        Assert.Equal(new Vector3(84f, 7.1f, 94f), local);

        cell = 0xA9B40100u;
        local = new Vector3(84f, 7.1f, 94f);
        Assert.True(LandDefs.AdjustToOutside(ref cell, ref local));
        Assert.Equal(0xA9B40019u, cell);
    }

    [Fact]
    public void Adjust_to_outside_snaps_tiny_negatives_at_the_0x00_edge_instead_of_leaving_the_map()
    {
        // |x|, |y| < 0.0002 snap to 0 first, so a position a hair west of the map edge stays on it.
        var cell = 0x00000001u;
        var local = new Vector3(-0.0001f, -0.0001f, 5f);
        Assert.True(LandDefs.AdjustToOutside(ref cell, ref local));
        Assert.Equal(0x00000001u, cell);
        Assert.Equal(new Vector3(0f, 0f, 5f), local);
    }

    [Theory]
    [InlineData(0x00000001u, -0.5f, 10f)] // west of the 0x00 edge
    [InlineData(0x00000001u, 10f, -0.5f)] // south of the 0x00 edge
    [InlineData(0x0000FFFFu, -24f, 0f)]
    [InlineData(0xFEFE0001u, 192f, 10f)] // east of the 0xFE edge: lcoord 2040 is landblock 0xFF
    [InlineData(0xFEFE0001u, 10f, 192f)] // north of the 0xFE edge
    public void Adjust_to_outside_fails_off_the_map_and_clears_the_cell_id(uint cellId, float x, float y)
    {
        var cell = cellId;
        var local = new Vector3(x, y, 0f);
        Assert.False(LandDefs.AdjustToOutside(ref cell, ref local));
        Assert.Equal(0u, cell);
    }

    [Fact]
    public void Adjust_to_outside_keeps_the_last_cell_of_the_0xFE_edge()
    {
        var cell = 0xFEFE0001u;
        var local = new Vector3(191.9f, 191.9f, 0f);
        Assert.True(LandDefs.AdjustToOutside(ref cell, ref local));
        Assert.Equal(0xFEFE0040u, cell);
        Assert.Equal(new Vector3(191.9f, 191.9f, 0f), local);
    }

    [Fact]
    public void Adjust_to_outside_refuses_an_invalid_cell_id()
    {
        var cell = 0xA9B4FFFEu;
        var local = new Vector3(10f, 10f, 0f);
        Assert.False(LandDefs.AdjustToOutside(ref cell, ref local));
        Assert.Equal(0u, cell);
    }

    [Theory]
    [InlineData(0xA9B40019u, 0xA9B40001u, 0f, 0f)] // same landblock
    [InlineData(0xA9B40100u, 0xA9B4FFFFu, 0f, 0f)] // an EnvCell and its landblock
    [InlineData(0xA9B40019u, 0xAAB30001u, 192f, -192f)]
    [InlineData(0xAAB30001u, 0xA9B40019u, -192f, 192f)]
    [InlineData(0xFE000001u, 0xFF000001u, 192f, 0f)] // defined even for the off-map landblock 0xFF
    [InlineData(0x00000001u, 0xFEFE0001u, 254f * 192f, 254f * 192f)]
    public void Block_offset_is_the_landblock_difference_in_metres(uint from, uint to, float x, float y)
    {
        Assert.Equal(new Vector3(x, y, 0f), LandDefs.GetBlockOffset(from, to));
    }
}
