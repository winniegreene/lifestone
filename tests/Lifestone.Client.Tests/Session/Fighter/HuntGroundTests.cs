using System.Numerics;
using System.Text.Json.Nodes;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The hunt grounds as data, no dats needed. M5 item 2 g3 (DoD 4, D42, D46): <see cref="HuntGround"/> reads its two rows and tables through <see cref="GroundCatalog"/> (the profiles <c>low-b-belt</c> and
/// <c>low-north-forest-mix</c>), and this class pins them to what the code held before, as LITERAL data (the rows are views over the profiles now, so a test against the view alone would be circular): the belt's
/// ten cells and 37 creatures unchanged (the melee journey's hunt on the belt is unchanged); the forest's 33 cells and 38 creatures the same set, now in a loop by landblock with each cell's encounter spot as its stop
/// point (D42, <c>m5-hunt-band-empty</c>) and the profile's cell cap. What the dats say of the cells' terrain is <see cref="HuntGroundDatTests"/>' and <c>HuntingGroundDatTests</c>'.
/// </summary>
public sealed class HuntGroundTests
{
    private static HuntGroundRow Forest => HuntGround.LowNorthForestMix;

    private static HuntGroundRow Belt => HuntGround.LowB;

    /// <summary>The weenie classes the archer never picks (<c>FighterTemplate.Archer</c>'s avoided set: they resist arrows), literal.</summary>
    private static readonly HashSet<uint> ArcherAvoids = [1756, 1758, 1760, 1761, 11528];

    /// <summary>Today's belt cells (HuntGround.LowB at m5/2 c17a24aa), literal, in the order the hunt visits them.</summary>
    private static readonly (uint Cell, double North, double East, double MapUnits, HuntTerrain Terrain)[] BeltCellsLiteral =
    [
        (0xA5B00032, 38.95, 30.65, 4.62, HuntTerrain.Dry),
        (0xA4B0003F, 39.45, 29.95, 4.76, HuntTerrain.Dry),
        (0xA5B0000C, 39.15, 30.15, 4.81, HuntTerrain.Dry),
        (0xA4B0003E, 39.35, 29.95, 4.82, HuntTerrain.Dry),
        (0xA6AF000D, 38.45, 30.95, 4.84, HuntTerrain.Dry),
        (0xA4B1000E, 40.15, 29.35, 4.86, HuntTerrain.Dry),
        (0xA6AF000C, 38.35, 30.95, 4.92, HuntTerrain.Dry),
        (0xA5AF003D, 38.45, 30.75, 4.95, HuntTerrain.Dry),
        (0xA4B10012, 39.75, 29.45, 4.98, HuntTerrain.Dry),
        (0xA3B1003F, 40.25, 29.15, 4.99, HuntTerrain.Dry),
    ];

    /// <summary>Today's belt table (HuntGround.LowBTable at m5/2 c17a24aa), literal: 37 rows, levels 4 to 30.</summary>
    private static readonly (uint Wcid, string Name, int Level)[] BeltTableLiteral =
    [
        (182, "Auroch Yearling", 4), (26684, "Blood Shrethlet", 7), (6, "Banderling Scout", 8),
        (4110, "Blood Shreth", 8), (215, "Blue Phyntos Wasp", 8), (192, "Drudge Prowler", 8),
        (1464, "Drudge Robber", 8), (7, "Drudge Skulker", 8), (193, "Drudge Slinker", 8),
        (940, "Drudge Sneaker", 8), (7990, "Field Ursuin", 8), (948, "Mosswart Feeder", 8),
        (2612, "Outcast Monouga", 8), (12017, "Red Phyntos Drone", 8), (12, "Red Phyntos Wasp", 8),
        (223, "Reedshark Pup", 8), (6535, "Small Shadow Child", 8), (20, "Auroch Bull", 9),
        (181, "Auroch Cow", 9), (26689, "Field Ursuin Cub", 9), (26687, "Hunter Shrethlet", 11),
        (221, "Adult Reedshark", 15), (937, "Banderling Guard", 15), (947, "Barker Mosswart", 15),
        (1631, "Drudge Servant", 15), (1632, "Drudge Slave", 15), (11989, "Drudge Vagabond", 15),
        (4111, "Hunter Shreth", 15), (1622, "Mire Phyntos Wasp", 15), (211, "Mudlurk Mosswart", 15),
        (12004, "Outcast Lord", 15), (18, "Reedshark Elder", 15), (222, "Veteran Reedshark", 15),
        (11537, "Virtuous Doll", 15), (8010, "Broken Fragment", 20), (946, "Mite Squire", 20),
        (233, "Tumerok Warrior", 30),
    ];

    /// <summary>The forest's 33 cells as they were written before the profile (nearest Holtburg first): the set the profile must hold.</summary>
    private static readonly (uint Cell, double North, double East, double MapUnits, HuntTerrain Terrain)[] ForestCellsLiteral =
    [
        (0x9FB50039, 42.85, 25.95, 7.66, HuntTerrain.Dry),
        (0x9FB30039, 41.25, 25.95, 7.75, HuntTerrain.Dry),
        (0x9FB60038, 44.35, 25.85, 7.97, HuntTerrain.Dry),
        (0x9FB10038, 40.35, 25.85, 8.04, HuntTerrain.Dry),
        (0x9FB6002F, 44.25, 25.75, 8.04, HuntTerrain.Dry),
        (0x9FB30021, 41.25, 25.65, 8.05, HuntTerrain.Dry),
        (0x9FB60030, 44.35, 25.75, 8.07, HuntTerrain.Dry),
        (0x9FB5001C, 43.15, 25.55, 8.08, HuntTerrain.Dry),
        (0x9FB20025, 40.85, 25.65, 8.12, HuntTerrain.Dry),
        (0x9FB60019, 43.65, 25.55, 8.13, HuntTerrain.Dry),
        (0x9FB70036, 44.95, 25.85, 8.13, HuntTerrain.Dry),
        (0x9FB40012, 42.15, 25.45, 8.16, HuntTerrain.Dry),
        (0x9FB50011, 42.85, 25.45, 8.16, HuntTerrain.Dry),
        (0x9FB70037, 45.05, 25.85, 8.16, HuntTerrain.Dry),
        (0x9FB50016, 43.35, 25.45, 8.19, HuntTerrain.Dry),
        (0x9FB2001E, 40.95, 25.55, 8.20, HuntTerrain.Dry),
        (0x9FB8003B, 45.45, 25.95, 8.20, HuntTerrain.Dry),
        (0x9FB70022, 44.55, 25.65, 8.21, HuntTerrain.Dry),
        (0x9FB70024, 44.75, 25.65, 8.26, HuntTerrain.Dry),
        (0x9FB8002A, 45.35, 25.75, 8.35, HuntTerrain.Dry),
        (0x9FB20013, 40.65, 25.45, 8.36, HuntTerrain.Dry),
        (0x9FB00036, 39.35, 25.85, 8.37, HuntTerrain.Dry),
        (0x9FB6000C, 43.95, 25.35, 8.38, HuntTerrain.Dry),
        (0x9FB7001E, 44.95, 25.55, 8.41, HuntTerrain.Dry),
        (0x9FB2000C, 40.75, 25.35, 8.43, HuntTerrain.Dry),
        (0x9FB70014, 44.75, 25.45, 8.45, HuntTerrain.Dry),
        (0x9FB20009, 40.45, 25.35, 8.50, HuntTerrain.Dry),
        (0x9FB10015, 40.05, 25.45, 8.51, HuntTerrain.Dry),
        (0x9FB7000B, 44.65, 25.35, 8.53, HuntTerrain.Dry),
        (0x9FB10014, 39.95, 25.45, 8.54, HuntTerrain.Dry),
        (0x9FB1000F, 40.25, 25.35, 8.55, HuntTerrain.Dry),
        (0x9FB60008, 44.35, 25.25, 8.55, HuntTerrain.Dry),
        (0x9FB70018, 45.15, 25.45, 8.57, HuntTerrain.Dry),
    ];

    /// <summary>Today's forest table (HuntGround.LowNorthForestTable at m5/2 c17a24aa), literal: 38 rows, levels 8 to 80.</summary>
    private static readonly (uint Wcid, string Name, int Level)[] ForestTableLiteral =
    [
        (2612, "Outcast Monouga", 8), (1756, "Shadow Child", 15), (1760, "Skeleton Warrior", 20),
        (1761, "Skeleton Captain", 20), (2574, "Rough Monouga", 20), (11528, "Elaniwood Golem", 20),
        (213, "Olthoi Nymph", 30), (235, "Goldenback Tusker", 30), (1627, "Tusker Crimsonback", 30),
        (1758, "Shadow", 30), (2575, "Crude Monouga", 30), (2576, "Wild Monouga", 30),
        (194, "Copper Golem", 40), (1608, "Drudge Lurker", 40), (1762, "Skeleton Lord", 40),
        (22009, "Olthoi Drone", 40), (24942, "Gotrok Lithos", 40), (27254, "Copper Golem Kingpin", 40),
        (28877, "Nasty Scarecrow", 40), (3, "Olthoi Worker", 50), (217, "Gold Phyntos Wasp", 50),
        (1609, "Drudge Stalker", 50), (1989, "Shadow Wisp", 50), (7128, "Sufut Zefir", 50),
        (7978, "Scrawed Grievver", 50), (7979, "Virulent Grievver", 50), (9251, "Brutish Monouga", 50),
        (22010, "Olthoi Servant", 50), (24940, "Gotrok Gigas", 50), (24959, "Olthoi Needler", 50),
        (28878, "Naughty Scarecrow", 50), (28879, "Nefarious Scarecrow", 50), (1630, "Lich Lord", 60),
        (1757, "Shadow Lieutenant", 60), (9253, "Wily Monouga", 60), (34102, "Old Zombie", 60),
        (1610, "Drudge Ravener", 80), (4253, "Panumbris Shadow", 80),
    ];

    /// <summary>The forest's hunt order (a loop by landblock from the spot nearest Holtburg) with each cell's encounter spot (cell x * 24, cell y * 24, from the pinned world SQL's encounter rows).</summary>
    private static readonly (uint Cell, float X, float Y)[] ForestLoopLiteral =
    [
        (0x9FB50039, 168f, 0f), (0x9FB50011, 48f, 0f), (0x9FB5001C, 72f, 72f), (0x9FB50016, 48f, 120f),
        (0x9FB60019, 72f, 0f), (0x9FB6000C, 24f, 72f), (0x9FB60008, 0f, 168f), (0x9FB6002F, 120f, 144f),
        (0x9FB60030, 120f, 168f), (0x9FB60038, 144f, 168f), (0x9FB70022, 96f, 24f), (0x9FB7000B, 24f, 48f),
        (0x9FB70014, 48f, 72f), (0x9FB70024, 96f, 72f), (0x9FB70036, 144f, 120f), (0x9FB70037, 144f, 144f),
        (0x9FB7001E, 72f, 120f), (0x9FB70018, 48f, 168f), (0x9FB8002A, 120f, 24f), (0x9FB8003B, 168f, 48f),
        (0x9FB40012, 48f, 24f), (0x9FB30021, 96f, 0f), (0x9FB30039, 168f, 0f), (0x9FB20025, 96f, 96f),
        (0x9FB2001E, 72f, 120f), (0x9FB2000C, 24f, 72f), (0x9FB20013, 48f, 48f), (0x9FB20009, 24f, 0f),
        (0x9FB1000F, 24f, 144f), (0x9FB10015, 48f, 96f), (0x9FB10014, 48f, 72f), (0x9FB10038, 144f, 168f),
        (0x9FB00036, 144f, 120f),
    ];
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
        Assert.Equal(ForestTableLiteral, table.Select(c => (c.Wcid, c.Name, c.Level)).ToArray()); // the profile's table is the code's table of before, row for row
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
    public void The_forest_is_a_used_row_of_33_cells_in_the_0x9FB_strip_starting_at_the_cell_nearest_holtburg()
    {
        Assert.True(Forest.InUse);
        Assert.Equal("Low North Forest Mix", Forest.Name);
        Assert.Equal(33, Forest.Cells.Count);
        Assert.Equal(Forest.Cells.Count, Forest.Cells.Select(c => c.CellId).Distinct().Count());
        Assert.All(Forest.Cells, c => Assert.Equal(0x9FBu, (c.CellId >> 16) >> 4));
        Assert.All(Forest.Cells, c => Assert.InRange(c.MapUnits, 7.6, 8.6)); // the strip is 7.7 to 9.2 map units west; the 33 nearest are 7.66 to 8.57
        Assert.Equal(0x9FB50039u, Forest.Cells[0].CellId);
        Assert.Equal(Forest.Cells.Min(c => c.MapUnits), Forest.Cells[0].MapUnits); // the loop starts at the cell nearest Holtburg
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

    // ------------------------------------------------------------------------------------------------ DoD 4: the belt's row and table equal today's, as literal data

    [Fact]
    public void The_belt_row_read_from_its_profile_equals_the_row_the_code_held_before_as_literal_data()
    {
        Assert.Equal("Low B belt", Belt.Name);
        Assert.Equal((8, 20), (Belt.MinLevel, Belt.MaxLevel));
        Assert.True(Belt.InUse);
        Assert.Equal(
            BeltCellsLiteral,
            Belt.Cells.Select(c => (c.CellId, c.North, c.East, c.MapUnits, c.Terrain)).ToArray()); // the same ten cells in the same order, the same figures
        Assert.Equal(BeltTableLiteral, Belt.Table.Select(c => (c.Wcid, c.Name, c.Level)).ToArray());
        Assert.Equal(BeltTableLiteral.Select(c => c.Name).Distinct(StringComparer.Ordinal), Belt.Creatures);
        Assert.Same(HuntGround.LowBTable, Belt.Table);
        Assert.Equal(34, Belt.InBandCount);
        Assert.Equal(10, Belt.UsableCells.Count());
    }

    [Fact]
    public void The_belt_stops_at_its_cells_centres_and_its_on_foot_cap_is_eight_as_before()
    {
        Assert.All(Belt.Cells, c => Assert.Equal(new Vector2(c.X, c.Y), c.Spot)); // the profile's stop point is the centre the hunt has always placed at
        Assert.All(Belt.Cells, c => Assert.Equal((c.X, c.Y), (c.StopX, c.StopY)));
        Assert.Equal((156f, 36f), (Belt.Cells[0].X, Belt.Cells[0].Y)); // 0x32 is index 49: x = 6 cells of 24 m plus 12, y = 1 cell plus 12
        Assert.Equal(HuntScenario.OnFootMaxCells, Belt.CellCap);
        Assert.Equal(new Vector3(156f, 36f, 40.5f), HuntGround.StopPoint(Belt.Cells[0], 40f));
        Assert.Equal(HuntGround.PlacePoint(Belt.Cells[0], 40f), HuntGround.StopPoint(Belt.Cells[0], 40f));
    }

    // ------------------------------------------------------------------------------------------------ D42: the forest's every spot

    [Fact]
    public void The_forest_holds_todays_33_cells_with_todays_figures_as_a_set()
    {
        var byCell = Forest.Cells.ToDictionary(c => c.CellId);
        Assert.Equal(ForestCellsLiteral.Select(c => c.Cell).Order(), byCell.Keys.Order());
        foreach (var (cell, north, east, mapUnits, terrain) in ForestCellsLiteral)
        {
            var read = byCell[cell];
            Assert.Equal((north, east, mapUnits, terrain), (read.North, read.East, read.MapUnits, read.Terrain));
        }
    }

    [Fact]
    public void The_forests_cells_are_a_loop_by_landblock_each_with_its_encounter_spot_as_its_stop_point()
    {
        Assert.Equal(ForestLoopLiteral.Select(c => c.Cell), Forest.Cells.Select(c => c.CellId)); // the hunt's order, literal
        Assert.Equal(ForestLoopLiteral.Select(c => new Vector2(c.X, c.Y)), Forest.Cells.Select(c => c.Spot!.Value)); // (cell x * 24, cell y * 24): the generator's corner
        Assert.Equal(ForestLoopLiteral.Select(c => (c.X, c.Y)), Forest.Cells.Select(c => (c.StopX, c.StopY)));

        // by landblock: a landblock's spots are visited together, none comes back; the first landblock is the nearest cell's, then north, then back down from the one below it
        var landblocks = Forest.Cells.Select(c => (ushort)(c.CellId >> 16)).ToArray();
        var runs = landblocks.Zip(landblocks.Skip(1)).Count(p => p.First != p.Second) + 1;
        Assert.Equal(landblocks.Distinct().Count(), runs);
        Assert.Equal(new ushort[] { 0x9FB5, 0x9FB6, 0x9FB7, 0x9FB8, 0x9FB4, 0x9FB3, 0x9FB2, 0x9FB1, 0x9FB0 }, landblocks.Distinct().ToArray());

        // each spot is the low corner of its own cell: the centre is 12 m further in x and y, 17 m away (m5-plan D42)
        foreach (var cell in Forest.Cells)
        {
            Assert.Equal((cell.X - 12f, cell.Y - 12f), (cell.StopX, cell.StopY));
            Assert.InRange(Vector2.Distance(new Vector2(cell.X, cell.Y), new Vector2(cell.StopX, cell.StopY)), 16.9, 17.0);
        }

        Assert.Equal(new Vector3(168f, 0f, 100.5f), HuntGround.StopPoint(Forest.Cells[0], 100f)); // the hunt goes to the spot ...
        Assert.Equal(new Vector3(180f, 12f, 100.5f), HuntGround.PlacePoint(Forest.Cells[0], 100f)); // ... a station's place stays at the centre
    }

    [Fact]
    public void The_forest_loop_is_about_3_2_km_between_its_spots_and_its_cap_is_every_cell()
    {
        static Vector2 Global(HuntCell c) => new((((c.CellId >> 24) & 0xFF) * 192f) + c.StopX, (((c.CellId >> 16) & 0xFF) * 192f) + c.StopY);

        var metres = Forest.Cells.Zip(Forest.Cells.Skip(1)).Sum(p => Vector2.Distance(Global(p.First), Global(p.Second)));
        Assert.InRange(metres, 3100, 3250); // 3,174 m for the 33 spots; the old eight cells alone were 300 to 1,000 m apart
        Assert.Equal(33, Forest.CellCap);
        Assert.Equal(Forest.Cells.Count, Forest.CellCap);
        Assert.NotEqual(HuntScenario.OnFootMaxCells, Forest.CellCap);
    }

    // ------------------------------------------------------------------------------------------------ the expected yield (report-only)

    [Fact]
    public void The_archers_band_takes_0_167_of_a_forest_spots_roll_and_the_rows_own_band_about_a_third()
    {
        var archer = Forest with { MinLevel = 20, MaxLevel = 30, Table = [.. Forest.Table.Where(c => !ArcherAvoids.Contains(c.Wcid))] };

        Assert.Equal(0.167, archer.ExpectedYield(), 6); // research 2: 213 0.040, 2576 0.038, 1627 0.035, 235 0.030, 2575 0.021, 2574 0.003
        var usable = archer.Table.Where(c => c.Level is >= 20 and <= 30).ToDictionary(c => c.Wcid, c => Math.Round(c.SpawnMass, 6));
        Assert.Equal(new Dictionary<uint, double> { [213] = 0.04, [2576] = 0.038, [1627] = 0.035, [235] = 0.03, [2575] = 0.021, [2574] = 0.003 }, usable);
        Assert.Equal(0.34236, Forest.ExpectedYield(), 6); // the row's own 30-40
        Assert.Equal(Forest.ExpectedYield(), GroundCatalog.Get(HuntGround.ForestKey).Outdoor!.ExpectedYieldPerSpot, 6); // the profile's figure is that sum
        Assert.Equal(Belt.ExpectedYield(), GroundCatalog.Get(HuntGround.BeltKey).Outdoor!.ExpectedYieldPerSpot, 6);
        Assert.Equal(0.9802, Belt.ExpectedYield(), 6);
    }

    [Fact]
    public void Eight_spots_are_all_empty_about_one_run_in_four_and_thirty_three_almost_never_for_the_archer()
    {
        const double PerSpot = 0.167;
        Assert.InRange(Math.Pow(1 - PerSpot, 8), 0.22, 0.24); // D42: 0.833^8 = 0.23, the three failures in seven logs
        Assert.True(Math.Pow(1 - PerSpot, Forest.CellCap!.Value) < 0.003); // 0.833^33
    }

    [Fact]
    public void Every_creature_has_a_spawn_mass_and_a_roll_never_exceeds_a_whole()
    {
        Assert.All(Belt.Table, c => Assert.InRange(c.SpawnMass, 0.0009, 0.13));
        Assert.All(Forest.Table, c => Assert.InRange(c.SpawnMass, 0.0004, 0.08));
        Assert.InRange(Belt.Table.Sum(c => c.SpawnMass), 0.9, 1.1); // camps and their young are counted together, so a roll's creatures sum to about a whole
        Assert.InRange(Forest.Table.Sum(c => c.SpawnMass), 0.9, 1.0); // 0.98 less the NPC (0.02) and the statue (0.001) that stay out of the table
    }

    // ------------------------------------------------------------------------------------------------ the profiles themselves

    [Theory]
    [InlineData(HuntGround.BeltKey, "Low B belt", 6, 10, 8, 8, 20)]
    [InlineData(HuntGround.ForestKey, "Low North Forest Mix", 9, 33, 33, 30, 40)]
    public void The_profile_is_an_outdoor_ground_of_the_rows_landblocks_with_every_section_sourced(string key, string name, int landblocks, int cells, int cap, int bandMin, int bandMax)
    {
        var ground = GroundCatalog.Get(key);
        Assert.Equal(GroundKind.Outdoor, ground.Kind);
        Assert.Equal(name, ground.Name);
        Assert.Equal(landblocks, ground.Landblocks.Count);
        Assert.Equal(cells, ground.Outdoor!.Cells.Count);
        Assert.Equal(cap, ground.Outdoor.CellCap);
        Assert.Equal((bandMin, bandMax), (ground.LevelBand.Min, ground.LevelBand.Max));
        Assert.Equal(HuntingGround.SectionNames.Order(), ground.Sources.Keys.Order());
        Assert.All(ground.Sources.Values, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal("Holtburg", ground.Home.Town);
        Assert.Equal(0xA9B40019u, ground.Home.LifestoneCell);
        Assert.Null(ground.Portal);
        Assert.Empty(ground.Storeys);
        Assert.Equal(ground.Outdoor.Cells.Select(c => (ushort)(c.Cell >> 16)).Distinct().Order(), ground.Landblocks.Order());
        Assert.All(ground.Outdoor.Cells, c => Assert.True(ground.InLandblocks(c.Cell)));
        Assert.Equal(HuntScenario.MeleeRange, ground.Outdoor.AcquireRadiusMetres); // the melee range; a missile hunt's 35 m is the weapon's
        Assert.Same(ground, GroundCatalog.All.Single(g => g.Key == key));
    }

    [Theory]
    [InlineData(HuntGround.BeltKey)]
    [InlineData(HuntGround.ForestKey)]
    public void A_creature_without_its_spawn_mass_makes_the_profile_fail_at_load_naming_the_field(string key)
    {
        var file = key + ".json";
        var copy = JsonNode.Parse(GroundCatalog.ResourceText(file)!)!;
        Assert.NotNull(GroundCatalog.Parse(file, copy.ToJsonString())); // the copy itself loads
        ((JsonObject)copy["outdoor"]!["creatures"]![0]!).Remove("spawn_mass");

        var e = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(file, copy.ToJsonString()));

        Assert.Equal("$.outdoor.creatures[0].spawn_mass", e.JsonPath);
        Assert.Equal(file, e.File);
        ((JsonObject)copy["outdoor"]!["creatures"]![0]!)["spawn_mass"] = 1.5;
        Assert.Equal("$.outdoor.creatures[0].spawn_mass", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(file, copy.ToJsonString())).JsonPath); // a chance over 1 is out of range
    }
}
