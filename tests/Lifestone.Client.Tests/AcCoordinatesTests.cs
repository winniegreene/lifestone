using System.Numerics;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Tests;

public sealed class AcCoordinatesTests
{
    [Theory]
    // Holtburg's town drop point (ACViewer Locations.txt): cell (3, 0) of 0xA9B4 is lcoord (1355, 1440).
    [InlineData(0xA9B40019u, "42.1N, 33.6E")]
    // Arwic's drop point: cell (1, 0) of 0xC6A9 is lcoord (1585, 1352).
    [InlineData(0xC6A90009u, "33.3N, 56.6E")]
    // Map corners: lcoord 0 is -102.4 + 0.5, lcoord 2039 is 101.5 + 0.5.
    [InlineData(0x00000001u, "101.9S, 101.9W")]
    [InlineData(0xFEFE0040u, "102.0N, 102.0E")]
    // lcoord 1019 -> -0.5 + 0.5 = 0: no letter, as retail prints an empty string at exactly 0.
    [InlineData(0x7F7F001Cu, "0.0, 0.0")]
    public void Map_coordinates_follow_retail_InqPlayerCoords_and_CellidToCoordinateString(uint cellId, string expected)
    {
        Assert.True(AcCoordinates.TryGetMapCoordinates(cellId, out var map));
        Assert.Equal(expected, map.ToString());
    }

    [Fact]
    public void Map_coordinates_of_the_zero_cell_are_exactly_zero()
    {
        // lcoord (1019, 1019): 0x7F block, cell offset 3 -> low = 3 * 8 + 3 + 1 = 0x1C.
        var cellId = AcCoordinates.LandCoordToCellId(1019, 1019);
        Assert.Equal(0x7F7F001Cu, cellId);
        Assert.True(AcCoordinates.TryGetMapCoordinates(cellId, out var map));
        Assert.Equal(0.0, map.NorthSouth, 12);
        Assert.Equal(0.0, map.EastWest, 12);
    }

    [Theory]
    [InlineData(0xA9B40100u)] // an EnvCell
    [InlineData(0xA9B40000u)] // not a cell
    [InlineData(0xA9B40041u)] // past the 64 outdoor cells
    public void Only_outdoor_cells_have_map_coordinates(uint cellId)
    {
        Assert.False(AcCoordinates.TryGetMapCoordinates(cellId, out _));
    }

    [Fact]
    public void Lcoord_and_outdoor_cell_ids_round_trip_over_the_whole_map()
    {
        for (var x = 0; x < AcCoordinates.LandCoordsPerSide; x++)
        {
            for (var y = 0; y < AcCoordinates.LandCoordsPerSide; y++)
            {
                var id = AcCoordinates.LandCoordToCellId(x, y);
                if (!AcCoordinates.TryGetLandCoord(id, out var rx, out var ry) || rx != x || ry != y)
                {
                    Assert.Fail($"({x}, {y}) -> 0x{id:X8} -> ({rx}, {ry})");
                }
            }
        }

        Assert.Equal(0u, AcCoordinates.LandCoordToCellId(-1, 0));
        Assert.Equal(0u, AcCoordinates.LandCoordToCellId(0, 2040));
    }

    [Fact]
    public void Cell_ids_name_the_landblock_and_the_cell_column_major()
    {
        // Cell index = cellX * 8 + cellY + 1 (ScenePlacement.CellId, ACE LandblockStruct).
        Assert.Equal(0xA9B40001u, AcCoordinates.LandCoordToCellId(0xA9 * 8, 0xB4 * 8));
        Assert.Equal(0xA9B40002u, AcCoordinates.LandCoordToCellId(0xA9 * 8, (0xB4 * 8) + 1));
        Assert.Equal(0xA9B40009u, AcCoordinates.LandCoordToCellId((0xA9 * 8) + 1, 0xB4 * 8));
        Assert.Equal(0xA9B40040u, AcCoordinates.LandCoordToCellId((0xA9 * 8) + 7, (0xB4 * 8) + 7));
    }

    [Fact]
    public void Global_positions_and_outdoor_cells_round_trip()
    {
        var holtburg = WorldLocations.Holtburg.Position;
        var global = AcCoordinates.ToGlobal(holtburg);
        Assert.Equal((0xA9 * 192.0) + 84.0, global.X, 4);
        Assert.Equal((0xB4 * 192.0) + 7.1, global.Y, 4);
        Assert.True(AcCoordinates.TryGetOutdoorCell(global, out var cell));
        Assert.Equal(holtburg.CellId, cell.CellId);
        Assert.Equal(holtburg.Local, cell.Local);

        var random = new Random(1234);
        for (var i = 0; i < 10_000; i++)
        {
            var g = new GlobalPosition(random.NextDouble() * 48_960.0, random.NextDouble() * 48_960.0, (random.NextDouble() * 800.0) - 100.0);
            Assert.True(AcCoordinates.TryGetOutdoorCell(g, out var c));
            Assert.True(AcCoordinates.IsOutdoorCell(c.CellId));
            Assert.InRange(c.Local.X, 0f, 192f);
            Assert.InRange(c.Local.Y, 0f, 192f);
            var back = AcCoordinates.ToGlobal(c);
            Assert.True(Math.Abs(back.X - g.X) < 1e-3 && Math.Abs(back.Y - g.Y) < 1e-3, $"{g} -> {c} -> {back}");
            Assert.Equal(AcCoordinates.LandblockAt(g), c.Landblock);
        }

        Assert.False(AcCoordinates.TryGetOutdoorCell(new GlobalPosition(-0.001, 10, 0), out _));
        Assert.False(AcCoordinates.TryGetOutdoorCell(new GlobalPosition(10, 48_960.0, 0), out _));
        Assert.Null(AcCoordinates.LandblockAt(new GlobalPosition(48_960.0, 0, 0)));
        Assert.Equal(new LandblockCoord(0xFE, 0x00), AcCoordinates.ClampedLandblockAt(new GlobalPosition(1e9, -5, 0)));
    }

    [Fact]
    public void Adjust_to_outside_moves_a_position_that_left_its_landblock_like_retail()
    {
        // 0xA9B4 cell (3, 0) with an origin 200 m east and 5 m south of the landblock corner: lcoord
        // (1352 + 8, 1440 - 1) = (1360, 1439) -> landblock 0xAAB3, cell (0, 7) = low 0x08; origin wrapped mod 192.
        var cellId = 0xA9B40019u;
        var local = new Vector3(200f, -5f, 12f);
        Assert.True(AcCoordinates.AdjustToOutside(ref cellId, ref local));
        Assert.Equal(0xAAB30008u, cellId);
        Assert.Equal(new Vector3(8f, 187f, 12f), local);

        // |x| below 0.0002 snaps to 0 first, so a tiny negative x stays in the same landblock.
        cellId = 0xA9B40001u;
        local = new Vector3(-0.0001f, 5f, 0f);
        Assert.True(AcCoordinates.AdjustToOutside(ref cellId, ref local));
        Assert.Equal(0xA9B40001u, cellId);
        Assert.Equal(0f, local.X);

        // Env cells adjust from their landblock too; invalid ids and positions off the map fail and zero the id.
        cellId = 0xA9B40105u;
        local = new Vector3(30f, 30f, -6f);
        Assert.True(AcCoordinates.AdjustToOutside(ref cellId, ref local));
        Assert.Equal(0xA9B4000Au, cellId);

        cellId = 0xA9B40000u;
        Assert.False(AcCoordinates.AdjustToOutside(ref cellId, ref local));
        Assert.Equal(0u, cellId);

        cellId = 0x00000001u;
        local = new Vector3(-30f, 1f, 0f);
        Assert.False(AcCoordinates.AdjustToOutside(ref cellId, ref local));
        Assert.Equal(0u, cellId);
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(1.0, 0.0, 90.0)]
    [InlineData(0.0, -1.0, 180.0)]
    [InlineData(-1.0, 0.0, 270.0)]
    [InlineData(1.0, 1.0, 45.0)]
    [InlineData(0.0, 0.0, 0.0)]
    public void Headings_are_compass_degrees_clockwise_from_north(double x, double y, double expected)
    {
        Assert.Equal(expected, AcCoordinates.HeadingOf(x, y), 9);
        if (x != 0 || y != 0)
        {
            var direction = AcCoordinates.DirectionOfHeading(expected);
            var length = Math.Sqrt((x * x) + (y * y));
            Assert.Equal(x / length, direction.X, 5);
            Assert.Equal(y / length, direction.Y, 5);
        }
    }

    [Fact]
    public void Landblock_origin_is_exact()
    {
        var origin = AcCoordinates.LandblockOrigin(new LandblockCoord(0xFE, 0xFE));
        Assert.Equal(254 * 192.0, origin.X);
        Assert.Equal(254 * 192.0, origin.Y);
        Assert.True(AcCoordinates.IsOnMap(new LandblockCoord(0xFE, 0xFE)));
        Assert.False(AcCoordinates.IsOnMap(new LandblockCoord(0xFF, 0x00)));
    }
}
