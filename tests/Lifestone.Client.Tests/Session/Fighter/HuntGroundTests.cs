using Lifestone.Client.Session.Fighter;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The hunt grounds as data, no dats needed (M4 3.1 s0): the Low North Forest Mix's creature table (the lead's read-only SELECT of generator 21183 on the pinned release v0.9.296, <c>cache/m4/3-1/forest-creatures.tsv</c>,
/// 40 rows less the NPC and the statue), its band 30 to 40, its cells (the 0x9FB* strip of <c>forest-candidates.tsv</c>, nearest first) and that it is a used row. What the dats say of the cells' terrain is
/// <see cref="HuntGroundDatTests"/>'s.
/// </summary>
public sealed class HuntGroundTests
{
    private static HuntGroundRow Forest => HuntGround.LowNorthForestMix;

    [Fact]
    public void The_forest_table_holds_the_38_creatures_of_the_select_by_wcid_in_level_then_wcid_order()
    {
        var table = HuntGround.LowNorthForestTable;

        Assert.Same(table, Forest.Table);
        Assert.Equal(38, table.Count);
        Assert.Equal(38, table.Select(c => c.Wcid).Distinct().Count());
        Assert.Equal(table.OrderBy(c => c.Level).ThenBy(c => c.Wcid).Select(c => c.Wcid), table.Select(c => c.Wcid));
        Assert.Equal((8, 80), (table.Min(c => c.Level), table.Max(c => c.Level)));
        Assert.Equal(
            new (int Level, int Count)[] { (8, 1), (15, 1), (20, 4), (30, 6), (40, 7), (50, 13), (60, 4), (80, 2) },
            table.GroupBy(c => c.Level).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())).ToArray());
        Assert.Equal(table.Select(c => c.Name).Distinct(StringComparer.Ordinal), Forest.Creatures);
    }

    [Fact]
    public void The_band_is_30_to_40_and_thirteen_of_the_table_are_in_it()
    {
        Assert.Equal((30, 40), (Forest.MinLevel, Forest.MaxLevel));
        Assert.Equal(13, Forest.InBandCount);
        Assert.Equal(
            ["Olthoi Nymph", "Goldenback Tusker", "Tusker Crimsonback", "Shadow", "Crude Monouga", "Wild Monouga", "Copper Golem", "Drudge Lurker", "Skeleton Lord", "Olthoi Drone", "Gotrok Lithos", "Copper Golem Kingpin", "Nasty Scarecrow"],
            HuntGround.LowNorthForestTable.Where(c => c.Level is >= 30 and <= 40).Select(c => c.Name).ToArray());
    }

    [Fact]
    public void LevelOf_reads_the_table_a_tusker_is_30_a_golem_40_and_an_unlisted_wcid_is_null()
    {
        Assert.Equal(30, Forest.LevelOf(1627)); // Tusker Crimsonback
        Assert.Equal(30, Forest.LevelOf(235)); // Goldenback Tusker
        Assert.Equal(40, Forest.LevelOf(194)); // Copper Golem
        Assert.Equal(40, Forest.LevelOf(27254)); // Copper Golem Kingpin
        Assert.Equal(50, Forest.LevelOf(24959)); // Olthoi Needler: listed, over the band
        Assert.Equal(20, Forest.LevelOf(1760)); // Skeleton Warrior: listed, under the band
        Assert.Null(Forest.LevelOf(1)); // not listed: never a target
    }

    [Fact]
    public void The_npc_and_the_statue_are_not_in_the_table()
    {
        Assert.Null(Forest.LevelOf(411)); // Fisana the Jeweler, an NPC
        Assert.Null(Forest.LevelOf(22641)); // Tusker Shrine Statue, level 171
        Assert.DoesNotContain(HuntGround.LowNorthForestTable, c => c.Name is "Fisana the Jeweler" or "Tusker Shrine Statue");
        Assert.DoesNotContain("Fisana the Jeweler", Forest.Creatures);
        Assert.DoesNotContain("Tusker Shrine Statue", Forest.Creatures);
    }

    [Fact]
    public void The_forest_is_a_used_row_with_its_cells_nearest_first_in_the_0x9FB_strip()
    {
        Assert.True(Forest.InUse);
        Assert.Equal("Low North Forest Mix", Forest.Name);
        Assert.Equal(33, Forest.Cells.Count);
        Assert.Equal(Forest.Cells.OrderBy(c => c.MapUnits).Select(c => c.CellId), Forest.Cells.Select(c => c.CellId));
        Assert.Equal(Forest.Cells.Count, Forest.Cells.Select(c => c.CellId).Distinct().Count());
        Assert.All(Forest.Cells, c => Assert.Equal(0x9FBu, (c.CellId >> 16) >> 4));
        Assert.All(Forest.Cells, c => Assert.InRange(c.MapUnits, 7.6, 8.6)); // the strip is 7.7 to 9.2 map units west; the 33 nearest are 7.66 to 8.57
        Assert.Equal(0x9FB50039u, Forest.Cells[0].CellId);
        Assert.Equal((42.85, 25.95, 7.66), (Forest.Cells[0].North, Forest.Cells[0].East, Forest.Cells[0].MapUnits));
        Assert.All(Forest.Cells, c => Assert.NotEqual(HuntTerrain.Unread, c.Terrain)); // each cell carries the dat test's finding
    }

    [Fact]
    public void Both_rows_are_listed_and_the_belt_keeps_its_band_and_table()
    {
        Assert.Equal([HuntGround.LowB, HuntGround.LowNorthForestMix], HuntGround.Rows);
        Assert.True(HuntGround.LowB.InUse);
        Assert.Equal((8, 20), (HuntGround.LowB.MinLevel, HuntGround.LowB.MaxLevel));
        Assert.Equal(37, HuntGround.LowBTable.Count);
        Assert.Null(HuntGround.LowB.LevelOf(1627)); // a tusker of the forest is not a belt creature
        Assert.Null(Forest.LevelOf(182)); // an Auroch Yearling of the belt is not a forest creature
    }
}
