using Lifestone.Client.Session.Fighter;
using Lifestone.Data.Physics;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The hunt grounds' cells against the retail dats: the ten belt cells (M4 1.2 s4a; closes <c>TODO(m4-belt-cell-terrain)</c>) and the forest's 0x9FB* strip (M4 3.1 s0; closes <c>m4-forest-cells (closed)</c>): water by the terrain type of the cell's four corner vertices (types 16 to 20, ACE's table,
/// <c>LandblockStruct.cs:62</c>, the same the physics world reads) and slope by the surface normal under a 5 x 5 grid of points in the cell against <see cref="PhysicsGlobals.FloorZ"/>, the physics floor
/// (a normal steeper than that cannot be stood on). <see cref="HuntGround.LowB"/> carries the finding per cell; this test pins it. Finding (2026-10-20, the retail dats): all ten cells are dry and flat (lowest normal z 0.993 or more). The forest's finding (2026-10-21, the retail dats): all 33 cells of the 0x9FB* strip are dry and flat (lowest normal z 0.973 or more, centre heights 70 to 108 m). <c>TODO(m4-belt-cell-terrain)</c> is closed for water and slope; what the dats say of buildings and scenery on a cell is not read (<c>TODO(m4-belt-cell-objects)</c>: the server drops the player to the floor and the hunt walks nowhere, so it only matters for a spawn point inside a wall).
/// </summary>
public sealed class HuntGroundDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const int FirstWater = 16, LastWater = 20, Grid = 5;

    private static HuntTerrain Read(DatSetFixture fixture, HuntCell cell, out float lowestNormalZ, out float height)
    {
        var coord = LandblockCoord.FromId(cell.CellId);
        var terrain = fixture.Dats.GetLandblockTerrain(coord) ?? throw new InvalidOperationException("no terrain for " + coord);
        var index = (int)(cell.CellId & 0xFFFF) - 1;
        var cx = index / 8;
        var cy = index % 8;
        var water = false;
        for (var vx = cx; vx <= cx + 1; vx++)
        {
            for (var vy = cy; vy <= cy + 1; vy++)
            {
                water |= terrain.TerrainType(vx, vy) is >= FirstWater and <= LastWater;
            }
        }

        var sampler = new TerrainSampler(terrain, fixture.Dats.TerrainRegion.LandHeightTable);
        lowestNormalZ = 1f;
        for (var i = 0; i < Grid; i++)
        {
            for (var j = 0; j < Grid; j++)
            {
                var x = (cx * 24) + 1 + (i * 22f / (Grid - 1));
                var y = (cy * 24) + 1 + (j * 22f / (Grid - 1));
                lowestNormalZ = Math.Min(lowestNormalZ, sampler.GetNormal(x, y).Z);
            }
        }

        height = sampler.GetHeight(cell.X, cell.Y);
        return water ? HuntTerrain.Water : lowestNormalZ < PhysicsGlobals.FloorZ ? HuntTerrain.Steep : HuntTerrain.Dry;
    }

    [RequiresDatsFact]
    public void Each_belt_cell_is_pinned_as_dry_water_or_steep_by_the_dats()
    {
        foreach (var cell in HuntGround.LowB.Cells)
        {
            var read = Read(fixture, cell, out var normalZ, out var height);
            output.WriteLine($"{cell}: read {read}, lowest normal z {normalZ:0.000} (floor {PhysicsGlobals.FloorZ:0.000}), centre height {height:0.0}");
            Assert.True(read == cell.Terrain, $"{cell} reads {read} in the dats");
        }
    }

    [RequiresDatsFact]
    public void The_hunt_uses_the_dry_walkable_cells_in_the_tables_order_and_there_are_enough_of_them()
    {
        var usable = HuntGround.LowB.UsableCells.ToArray();
        Assert.True(usable.Length >= 4, "the hunt visits at most four cells; the dats leave " + usable.Length);
        var order = HuntGround.LowB.Cells.Where(c => c.Usable).Select(c => c.CellId).ToArray();
        Assert.Equal(order, usable.Select(c => c.CellId).ToArray());
        Assert.All(usable, c => Assert.Equal(HuntTerrain.Dry, c.Terrain));
        Assert.Equal(0xA5B00032u, HuntGround.LowB.Cells[0].CellId);
    }

    [RequiresDatsFact]
    public void The_cell_centre_is_what_the_capture_used_and_the_terrain_height_reads_for_every_cell()
    {
        var first = HuntGround.LowB.Cells[0];
        Assert.Equal((156f, 36f), (first.X, first.Y));
        foreach (var cell in HuntGround.LowB.Cells)
        {
            Assert.NotNull(HuntGround.TerrainHeight(fixture.Dats, cell));
            Assert.InRange(cell.X, 12f, 180f);
            Assert.InRange(cell.Y, 12f, 180f);
        }
    }

    [RequiresDatsFact]
    public void Each_forest_cell_is_pinned_as_dry_water_or_steep_by_the_dats_and_the_ten_nearest_usable_ones_are_dry()
    {
        // The 0x9FB* strip of cache/m4/3-1/forest-candidates.tsv, nearest first: the dats' finding per cell is pinned as the table has it (the cells nearer than the tenth dry one that are not dry are
        // marked Water or Steep as read); every disagreement is listed in one failure message so a wrong cell is found at once.
        var row = HuntGround.LowNorthForestMix;
        var disagree = new List<string>();
        foreach (var cell in row.Cells)
        {
            var read = Read(fixture, cell, out var normalZ, out var height);
            output.WriteLine($"{cell}: read {read}, lowest normal z {normalZ:0.000} (floor {PhysicsGlobals.FloorZ:0.000}), centre height {height:0.0}");
            if (read != cell.Terrain)
            {
                disagree.Add($"{cell} reads {read} (lowest normal z {normalZ:0.000})");
            }
        }

        Assert.True(disagree.Count == 0, "forest cells that the dats do not read as the table says: " + string.Join("; ", disagree));
        var usable = row.UsableCells.ToArray();
        Assert.True(usable.Length >= 10, "the forest row needs ten dry and walkable cells; the dats leave " + usable.Length + ", the others read: " + string.Join("; ", row.Cells.Where(c => !c.Usable)));
        Assert.Equal(row.Cells.MinBy(c => c.MapUnits)!.CellId, row.Cells[0].CellId); // the loop (M5 D42) starts at the cell nearest Holtburg
        // The first ten usable cells are the first ten cells: none of them was skipped for water or a slope, and each is pinned Dry.
        Assert.Equal(row.Cells.Take(10).Select(c => c.CellId), usable.Take(10).Select(c => c.CellId));
        Assert.All(usable.Take(10), c => Assert.Equal(HuntTerrain.Dry, c.Terrain));
        Assert.All(row.Cells, c => Assert.Equal(0x9FBu, (c.CellId >> 16) >> 4)); // the 0x9FB* landblocks
    }

    [RequiresDatsFact]
    public void The_forest_cell_centres_read_a_terrain_height_for_every_cell()
    {
        var first = HuntGround.LowNorthForestMix.Cells[0];
        Assert.Equal(0x9FB50039u, first.CellId);
        Assert.Equal((180f, 12f), (first.X, first.Y)); // 0x39 is index 56: x = 56 / 8 = 7 cells of 24 m plus 12, y = 56 % 8 = 0 plus 12
        foreach (var cell in HuntGround.LowNorthForestMix.Cells)
        {
            Assert.NotNull(HuntGround.TerrainHeight(fixture.Dats, cell));
        }
    }

    [Fact]
    public void The_forest_row_and_the_belt_are_both_used_rows()
    {
        Assert.True(HuntGround.LowNorthForestMix.InUse);
        Assert.True(HuntGround.LowB.InUse);
        Assert.Equal((8, 20), (HuntGround.LowB.MinLevel, HuntGround.LowB.MaxLevel));
        Assert.Equal(10, HuntGround.LowB.Cells.Count);
        Assert.Contains("Tumerok Warrior", HuntGround.LowB.Creatures);
    }

    [Fact]
    public void The_low_b_table_is_the_pinned_selects_rows_by_wcid_and_the_band_filters_the_tumerok()
    {
        // The lead's read-only SELECT on release v0.9.296 (M4 1.2 fix, review 1.2-R1): creatures (ItemType 16) of generators 1999 and 2001, three levels down, with Level (int 25): 37 rows, levels 4 to 30.
        var table = HuntGround.LowB.Table;
        Assert.Equal(37, table.Count);
        Assert.Equal(37, table.Select(c => c.Wcid).Distinct().Count());
        Assert.Equal(4, table.Min(c => c.Level));
        Assert.Equal(30, table.Max(c => c.Level));
        Assert.Equal(8, HuntGround.LowB.LevelOf(7990)); // Field Ursuin
        Assert.Equal(8, HuntGround.LowB.LevelOf(6)); // Banderling Scout
        Assert.Equal(15, HuntGround.LowB.LevelOf(1632)); // Drudge Slave
        Assert.Equal(20, HuntGround.LowB.LevelOf(946)); // Mite Squire
        Assert.Equal(30, HuntGround.LowB.LevelOf(233)); // Tumerok Warrior: listed, level 30
        Assert.Equal(4, HuntGround.LowB.LevelOf(182)); // Auroch Yearling: under the band
        Assert.Null(HuntGround.LowB.LevelOf(1)); // not listed: never a target
        Assert.Equal(34, HuntGround.LowB.InBandCount); // 37 less the levels 4, 7 and 30
        Assert.DoesNotContain(table, c => c.Level >= 8 && c.Level <= 20 && c.Name == "Tumerok Warrior");
    }
}
