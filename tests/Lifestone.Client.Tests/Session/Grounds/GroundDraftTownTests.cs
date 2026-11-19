using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Tests;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// <see cref="GroundDraft"/>'s town half and its exits by emote on a synthetic mysqldump excerpt (M5 item 2 g8; no dump, no dats): the entry portals are the weenies whose Destination lies in the landblock and the arrival is the
/// first's unless named; the portal's instance takes the outdoor cell its point lies in (x/24*8 + y/24 + 1); the home is the nearest outdoor lifestone (an indoor row is no candidate) or the named landblock's; the vendors are the
/// ones in the lifestone's 3x3 landblocks or within the town radius, with what they buy, their shop rows and the prices at their own multipliers; a creature whose Death or Use emote casts a spell with a destination, or
/// teleports, is an exit by emote and its slot cells are exit cells; the proposed roles and the level band from the targets only.
/// </summary>
public sealed class GroundDraftTownSqlTests
{
    private const string Cols = "`last_Modified` datetime NOT NULL";

    private static readonly string Excerpt = $$"""
        CREATE TABLE `weenie` (
          `class_Id` int(10) unsigned NOT NULL,
          `class_Name` varchar(100) NOT NULL,
          `type` int(5) NOT NULL DEFAULT '0',
          {{Cols}}
        ) ENGINE=InnoDB;
        INSERT INTO `weenie` VALUES (7,'statue',10,'2020-01-01 00:00:00'),(8,'bard',10,'2020-01-01 00:00:00'),(9,'imp',10,'2020-01-01 00:00:00'),(60,'denportal',7,'2020-01-01 00:00:00'),(70,'lifestone',25,'2020-01-01 00:00:00'),(80,'vendora',12,'2020-01-01 00:00:00'),(81,'vendorb',12,'2020-01-01 00:00:00'),(82,'vendorc',12,'2020-01-01 00:00:00'),(900,'gen',1,'2020-01-01 00:00:00');
        CREATE TABLE `landblock_instance` (
          `guid` int(10) unsigned NOT NULL,
          `landblock` int(5) GENERATED ALWAYS AS ((`obj_Cell_Id` >> 16)) VIRTUAL,
          `weenie_Class_Id` int(10) unsigned NOT NULL,
          `obj_Cell_Id` int(10) unsigned NOT NULL,
          `origin_X` float NOT NULL,
          `origin_Y` float NOT NULL,
          `origin_Z` float NOT NULL,
          `angles_W` float NOT NULL,
          `angles_X` float NOT NULL,
          `angles_Y` float NOT NULL,
          `angles_Z` float NOT NULL,
          `is_Link_Child` bit(1) NOT NULL,
          {{Cols}}
        ) ENGINE=InnoDB;
        INSERT INTO `landblock_instance` (`guid`, `weenie_Class_Id`, `obj_Cell_Id`, `origin_X`, `origin_Y`, `origin_Z`, `angles_W`, `angles_X`, `angles_Y`, `angles_Z`, `is_Link_Child`, `last_Modified`) VALUES ({{0x70010001}},900,{{0x01050101}},1,1,0,1,0,0,0,b'0','2020-01-01 00:00:00'),({{0x70010002}},7,{{0x01050102}},1,1,0,1,0,0,0,b'1','2020-01-01 00:00:00'),({{0x70010003}},8,{{0x01050103}},1,1,0,1,0,0,0,b'1','2020-01-01 00:00:00'),({{0x70010004}},9,{{0x01050104}},1,1,0,1,0,0,0,b'1','2020-01-01 00:00:00'),(1000,60,{{0x20300000}},57.7292,64.9681,70.005,1,0,0,0,b'0','2020-01-01 00:00:00'),(2000,70,{{0x20310000}},18.7945,137.695,144.005,1,0,0,0,b'0','2020-01-01 00:00:00'),(2001,70,{{0x40300000}},10,10,0,1,0,0,0,b'0','2020-01-01 00:00:00'),(2002,70,{{0x20300105}},5,5,0,1,0,0,0,b'0','2020-01-01 00:00:00'),(3000,80,{{0x20310144}},157.688,61.4069,220.005,1,0,0,0,b'0','2020-01-01 00:00:00'),(3001,81,{{0x22310102}},85.0908,132.949,219.245,1,0,0,0,b'0','2020-01-01 00:00:00'),(3002,82,{{0x50310000}},1,1,0,1,0,0,0,b'0','2020-01-01 00:00:00');
        CREATE TABLE `landblock_instance_link` (
          `id` int(10) unsigned NOT NULL,
          `parent_GUID` int(10) unsigned NOT NULL,
          `child_GUID` int(10) unsigned NOT NULL,
          {{Cols}}
        ) ENGINE=InnoDB;
        INSERT INTO `landblock_instance_link` VALUES (1,{{0x70010001}},{{0x70010002}},'2020-01-01 00:00:00'),(2,{{0x70010001}},{{0x70010003}},'2020-01-01 00:00:00'),(3,{{0x70010001}},{{0x70010004}},'2020-01-01 00:00:00');
        CREATE TABLE `weenie_properties_generator` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `probability` float NOT NULL,
          `weenie_Class_Id` int(10) unsigned NOT NULL,
          `delay` float DEFAULT NULL,
          `init_Create` int(10) NOT NULL,
          `max_Create` int(10) NOT NULL,
          `when_Create` int(10) unsigned NOT NULL,
          `where_Create` int(10) unsigned NOT NULL,
          `obj_Cell_Id` int(10) unsigned DEFAULT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_generator` VALUES (1,900,1,0,300,1,1,2,4,NULL);
        CREATE TABLE `weenie_properties_float` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `type` smallint(5) unsigned NOT NULL,
          `value` double NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_float` VALUES (1,80,38,1.8),(2,81,38,1.35),(3,900,41,30),(4,80,37,0.5);
        CREATE TABLE `weenie_properties_int` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `type` smallint(5) unsigned NOT NULL,
          `value` int(10) NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_int` VALUES (1,60,86,20),(2,7,25,171),(3,8,25,40),(4,9,25,50),(5,80,74,{{0x000CB000}}),(6,81,74,{{0x00044080}}),(7,690,19,1000),(8,20631,19,22),(9,379,19,170);
        CREATE TABLE `weenie_properties_string` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `type` smallint(5) unsigned NOT NULL,
          `value` text NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_string` VALUES (1,7,1,'Statue'),(2,8,1,'Bard'),(3,9,1,'Imp'),(4,60,1,'Den Portal'),(5,80,1,'Vendor A'),(6,81,1,'Vendor B'),(7,690,1,'Pyreal Scarab'),(8,20631,1,'Prismatic Taper'),(9,379,1,'Mana Potion');
        CREATE TABLE `weenie_properties_position` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `position_Type` smallint(5) unsigned NOT NULL,
          `obj_Cell_Id` int(10) unsigned NOT NULL,
          `origin_X` float NOT NULL,
          `origin_Y` float NOT NULL,
          `origin_Z` float NOT NULL,
          `angles_W` float NOT NULL,
          `angles_X` float NOT NULL,
          `angles_Y` float NOT NULL,
          `angles_Z` float NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_position` VALUES (1,60,2,{{0x01050101}},10,-20,0,0.707107,0,0,-0.707107),(2,60,1,{{0x20300000}},0,0,0,1,0,0,0);
        CREATE TABLE `weenie_properties_create_list` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `destination_Type` tinyint(5) NOT NULL,
          `weenie_Class_Id` int(10) unsigned NOT NULL,
          `stack_Size` int(10) NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_create_list` VALUES (1,80,4,690,-1),(2,80,4,20631,-1),(3,80,1,379,-1),(4,81,4,379,-1);
        CREATE TABLE `weenie_properties_emote` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `category` int(10) unsigned NOT NULL,
          `probability` float NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_emote` VALUES (1,7,7,1),(2,8,7,1),(3,9,3,1),(4,7,5,1);
        CREATE TABLE `weenie_properties_emote_action` (
          `id` int(10) unsigned NOT NULL,
          `emote_Id` int(10) unsigned NOT NULL,
          `order` int(10) unsigned NOT NULL,
          `type` int(10) unsigned NOT NULL,
          `spell_Id` int(10) DEFAULT NULL,
          `obj_Cell_Id` int(10) unsigned DEFAULT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_emote_action` VALUES (1,1,0,19,2936,NULL),(2,2,0,19,100,NULL),(3,3,0,100,NULL,{{0x7F000010}}),(4,4,0,19,2936,NULL);
        CREATE TABLE `spell` (
          `id` int(10) unsigned NOT NULL,
          `name` text,
          `position_Obj_Cell_ID` int(10) unsigned DEFAULT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `spell` VALUES (2936,'Entrance to Tusker Island',4135714867),(100,'Heal Self',NULL);
        """;

    /// <summary>The excerpt as the two passes would leave it: the first pass's filters, with every landblock's instances (the second pass's rows).</summary>
    private static DraftWorld World(uint? arrival, ushort? home = null)
    {
        var wanted = new Dictionary<string, Func<SqlRow, bool>>(GroundDraft.WorldTables(0x0105)) { ["landblock_instance"] = _ => true };
        using var reader = new StringReader(Excerpt);
        return GroundDraft.World(WorldSqlDump.Read(reader, wanted), 0x0105, arrival, home);
    }

    /// <summary>M6 3 a1: the excerpt with the entry portal's MaxLevel and a NoRecall PortalBitmask (0x31, the Tusker Burrow's), and the Bard's stat rows (the Imp has none).</summary>
    private static readonly string StatsExcerpt = Excerpt.Replace("INSERT INTO `weenie_properties_int` VALUES (1,60,86,20),", "INSERT INTO `weenie_properties_int` VALUES (1,60,86,20),(20,60,87,40),(21,60,111,49),", StringComparison.Ordinal) + "\n" + """
        CREATE TABLE `weenie_properties_attribute` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `type` smallint(5) unsigned NOT NULL,
          `init_Level` int(10) unsigned NOT NULL,
          `level_From_C_P` int(10) unsigned NOT NULL,
          `c_P_Spent` int(10) unsigned NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_attribute` VALUES (1,8,1,60,0,0),(2,8,2,40,1,0),(3,8,3,50,0,0),(4,8,4,45,0,0),(5,8,5,30,0,0),(6,8,6,40,0,0),(7,9,1,10,0,0);
        CREATE TABLE `weenie_properties_attribute_2nd` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `type` smallint(5) unsigned NOT NULL,
          `init_Level` int(10) unsigned NOT NULL,
          `level_From_C_P` int(10) unsigned NOT NULL,
          `c_P_Spent` int(10) unsigned NOT NULL,
          `current_Level` int(10) unsigned NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_attribute_2nd` VALUES (1,8,1,50,5,0,12),(2,8,3,99,0,0,99);
        CREATE TABLE `weenie_properties_skill` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `type` smallint(5) unsigned NOT NULL,
          `level_From_P_P` smallint(5) unsigned NOT NULL,
          `s_a_c` int(10) unsigned NOT NULL,
          `p_p` int(10) unsigned NOT NULL,
          `init_Level` int(10) unsigned NOT NULL,
          `resistance_At_Last_Check` int(10) unsigned NOT NULL,
          `last_Used_Time` double NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_skill` VALUES (1,8,15,2,2,0,10,0,0),(2,9,15,0,1,0,30,0,0);
        CREATE TABLE `weenie_properties_body_part` (
          `id` int(10) unsigned NOT NULL,
          `object_Id` int(10) unsigned NOT NULL,
          `key` smallint(5) unsigned NOT NULL,
          `d_Type` int(10) NOT NULL,
          `d_Val` int(10) NOT NULL,
          `d_Var` float NOT NULL
        ) ENGINE=InnoDB;
        INSERT INTO `weenie_properties_body_part` VALUES (1,8,0,4,10,0.5),(2,8,1,1,5,0.5),(3,8,2,64,0,0),(4,9,0,16,0,0);
        """;

    [Fact]
    public void The_entry_s_gate_and_tie_rule_and_the_creatures_stats_come_from_their_rows_and_a_missing_row_is_a_named_note_M6_3_a1()
    {
        var wanted = new Dictionary<string, Func<SqlRow, bool>>(GroundDraft.WorldTables(0x0105)) { ["landblock_instance"] = _ => true };
        using var reader = new StringReader(StatsExcerpt);
        var world = GroundDraft.World(WorldSqlDump.Read(reader, wanted), 0x0105, null);

        // The entry: MinLevel 20, MaxLevel 40, PortalBitmask 0x31 carries NoRecall (0x20), so the portal cannot be tied (ACE's Portal.NoTie).
        var entry = Assert.Single(world.Entries);
        Assert.Equal(((int?)20, (int?)40, (int?)0x31, false), (entry.MinLevel, entry.MaxLevel, entry.PortalBitmask, entry.Tieable));
        Assert.True(World(null).Entries[0].Tieable); // no bitmask row: Unrestricted

        // The Bard: attributes init + ranks (Endurance 40 + 1); health 50 + 5 + round(41 / 2 = 20.5, half up) = 76 (the row's current_Level 12 is not ACE's); Magic Defense, trained: 10 + 2 + (30 + 40) / 7 = 22;
        // the damage types of the parts with a d_Val, 4 | 1 (the 64 part has none). The Imp has one attribute row, an untrained skill and no attack part: each a named note, no value.
        var bard = world.Creatures.Single(c => c.Wcid == 8);
        Assert.Equal(new GroundAttributes(60, 41, 45, 50, 30, 40), bard.Attributes);
        Assert.Equal(((int?)76, (int?)22, (uint?)5u), (bard.MaxHealth, bard.MagicDefense, bard.DamageTypes));
        var imp = world.Creatures.Single(c => c.Wcid == 9);
        Assert.Equal(((GroundAttributes?)null, (int?)null, (int?)null, (uint?)null), (imp.Attributes, imp.MaxHealth, imp.MagicDefense, imp.DamageTypes));
        foreach (var name in new[] { "TODO(ground-draft-attributes): creature 9", "TODO(ground-draft-health): creature 9", "TODO(ground-draft-magic-defense): creature 9", "TODO(ground-draft-damage-types): creature 9" })
        {
            Assert.Contains(world.Notes, n => n.StartsWith(name, StringComparison.Ordinal));
        }

        Assert.Single(world.Notes, n => n.StartsWith("TODO(f11-swing-seconds):", StringComparison.Ordinal)); // the swing is never drafted, named once
    }

    [Fact]
    public void The_front_point_is_three_metres_along_the_portal_s_facing_with_the_z_to_the_centimetre()
    {
        // Tusker Temple's portal row (the pinned dump's guid 0x72A80000): w 0.999794, z -0.02029 is a yaw of -2.32 degrees, so +Y turned by it is (+0.0406, +0.9992); the profile's hand-derived point.
        var portal = new DraftPlace(0x72A80000, 22696, 0x2A800013, new Vector3(57.7292f, 64.9681f, 70.005f)) { HeadingW = 0.999794f, HeadingZ = -0.02029f };
        var (cell, front) = GroundDraft.FrontOf(portal);
        Assert.Equal(0x2A800013u, cell);
        Assert.Equal(57.8509, front.X, 0.0001);
        Assert.Equal(67.9656, front.Y, 0.0001);
        Assert.Equal(70.01f, front.Z);
        var (east, eastPoint) = GroundDraft.FrontOf(new DraftPlace(1, 1, 0x2A800001, new Vector3(22f, 1f, 0f)) { HeadingW = MathF.Cos(-MathF.PI / 4), HeadingZ = MathF.Sin(-MathF.PI / 4) }); // facing east (yaw -90 degrees)
        Assert.Equal(25f, eastPoint.X, 0.001f);
        Assert.Equal(0x2A800009u, east); // x 25 lies in the next column of outdoor cells
    }

    [Fact]
    public void The_entry_portals_are_the_weenies_whose_destination_lies_in_the_landblock_and_the_arrival_is_the_first_s_unless_named()
    {
        var world = World(null);
        var entry = Assert.Single(world.Entries);
        Assert.Equal(new DraftEntry(60, "Den Portal", 0x01050101, new Vector3(10, -20, 0), 0.707107f, -0.707107f, 20), entry); // the weenie's other position row (type 1) is no destination
        Assert.NotNull(world.Arrival);
        Assert.Equal((60u, 0x01050101u, 10f, -20f), (world.Arrival.Wcid, world.Arrival.Cell, world.Arrival.Origin.X, world.Arrival.Origin.Y));
        Assert.Equal(world.Arrival, World(0x01050101).Arrival); // naming the arrival cell gives the same
        Assert.Null(World(0x01050999).Arrival); // a cell no portal leads to: the draft's old note
        Assert.Contains(World(0x01050999).Notes, n => n.StartsWith("TODO(ground-draft-arrival): no portal's Destination is the arrival cell 0x01050999", StringComparison.Ordinal));
    }

    [Fact]
    public void The_portal_s_instance_takes_the_outdoor_cell_its_point_lies_in()
    {
        var town = World(null).Town;
        Assert.NotNull(town);
        Assert.Equal(new DraftPlace(1000, 60, 0x20300013, new Vector3(57.7292f, 64.9681f, 70.005f)), town.Portal); // x/24 = 2, y/24 = 2: 2 * 8 + 2 + 1 = 0x13
        Assert.Equal(0xA9B40019u, GroundDraft.OutdoorCell(0xA9B40000, new Vector3(81.3304f, 11.7974f, 94.005f))); // Holtburg's lifestone row
        Assert.Equal(0x27800006u, GroundDraft.OutdoorCell(0x27800000, new Vector3(18.7945f, 137.695f, 144.005f)));
        Assert.Equal(0xA9B40141u, GroundDraft.OutdoorCell(0xA9B40141, new Vector3(139.234f, 18.3293f, 94.005f))); // an indoor row keeps its own cell
        Assert.Equal(0x20300040u, GroundDraft.OutdoorCell(0x20300000, new Vector3(192f, 192f, 0f))); // a point on the landblock's far edge stays in its last cell
    }

    [Fact]
    public void The_home_is_the_nearest_outdoor_lifestone_or_the_named_landblock_s()
    {
        var town = World(null).Town!;
        Assert.Equal([2000u, 2001u], town.Lifestones.Select(l => l.Place.Guid)); // 2002 stands in an indoor cell: its landblock is a building's or a dungeon's, so no distance to it means anything
        Assert.Equal(0x20310006u, town.Lifestone!.Place.Cell);
        Assert.InRange(town.Lifestone.Metres, 267.0, 268.0); // one landblock north and 38.9 m west of the portal's x, 264.8 m north of its y
        var named = World(null, 0x4030).Town!;
        Assert.Equal([2001u], named.Lifestones.Select(l => l.Place.Guid));
        Assert.Empty(named.Vendors); // nothing near the far lifestone
        Assert.Contains(World(null, 0x7777).Notes, n => n.StartsWith("TODO(ground-draft-home): no lifestone stands in landblock 0x7777", StringComparison.Ordinal));
    }

    [Fact]
    public void The_vendors_are_those_in_the_3x3_or_within_the_town_radius_with_their_own_prices()
    {
        var vendors = World(null).Town!.Vendors;
        Assert.Equal([3000u, 3001u], vendors.Select(v => v.Place.Guid)); // 3002 stands 30 landblocks away
        var near = vendors[0];
        Assert.Equal(("Vendor A", 80u, 0x20310144u, 0x000CB000u, 1.8, true), (near.Name, near.Place.Wcid, near.Place.Cell, near.BuysItemTypes, near.SellMultiplier, near.InThreeByThree));
        Assert.Equal([new DraftWare(690, "Pyreal Scarab", 1000, 1800), new DraftWare(20631, "Prismatic Taper", 22, 40)], near.Wares); // the create-list row of another destination is no ware
        var far = vendors[1];
        Assert.False(far.InThreeByThree); // two landblocks east: within the radius, outside the 3x3
        Assert.InRange(far.Metres, 449.0, 451.0);
        Assert.Equal([new DraftWare(379, "Mana Potion", 170, 230)], far.Wares);
        Assert.Equal((30, 40, 230, 1), (GroundDraft.PriceOf(1.35, 22), GroundDraft.PriceOf(1.8, 22), GroundDraft.PriceOf(1.35, 170), GroundDraft.PriceOf(1.8, 0))); // the den's taper 30, the fort's 40, the potion 230, the least price 1
    }

    [Fact]
    public void A_creature_whose_death_or_use_emote_teleports_is_an_exit_by_emote_and_its_slot_cells_are_exit_cells()
    {
        var world = World(null);
        Assert.Equal(
            [
                (7u, "Statue", 7u, 19u, 2936u, "Entrance to Tusker Island", 4135714867u, "0x01050102"),
                (9u, "Imp", 3u, 100u, 0u, "TeleportSelf", 0x7F000010u, "0x01050104"),
            ],
            world.EmoteExits.Select(e => (e.Wcid, e.Name, e.Category, e.ActionType, e.Spell, e.SpellName, e.Destination, string.Join(",", e.Cells.Select(c => $"0x{c:X8}"))))); // the bard's Use emote casts a spell with no destination, and the statue's HeartBeat emote is no player's act
        Assert.Equal(["Use", "Death"], world.EmoteExits.Select(e => e.CategoryName));
        Assert.Equal([0x01050102u, 0x01050104u], world.ExitCells);
        Assert.Equal((GroundRole.Avoid, GroundRole.Target, GroundRole.Avoid), (GroundDraft.ProposedRole(world, 7), GroundDraft.ProposedRole(world, 8), GroundDraft.ProposedRole(world, 9)));
        Assert.Equal(new LevelBand(40, 40), GroundDraft.LevelBandOf(world.Creatures.Where(c => GroundDraft.ProposedRole(world, c.Wcid) == GroundRole.Target).Select(c => c.Level!.Value))); // the targets' levels only, not the statue's 171 or the imp's 50
        Assert.Null(GroundDraft.LevelBandOf([]));
    }
}

/// <summary>
/// The town half of the draft against the two dungeon profiles, on the pinned world SQL (M5 item 2 g8, D46; read-only, a few seconds a case): each profile's hand-set portal, home and vendors equal the draft's, or the
/// difference is named here with why the hand-set value wins; the exits by emote and the level band are the draft's rule.
/// </summary>
public sealed class GroundDraftTownTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string DenKey = "black-spawn-den-aluvian";

    /// <summary>M6 item 14: the den of the entrance at 60.0S 57.6W (landblock 0x0103), the Aluvian den's copy (<c>DenCopyDatTests</c>, <c>DenCopySqlTests</c>): every rule that is the den's here is its too.</summary>
    private const string ShoDenKey = "black-spawn-den-sho";

    private static bool IsDen(string key) => key is DenKey or ShoDenKey;

    /// <summary>M6 3 p1 (D48): the four spine tusker grounds are hunted from Holtburg like the den (the island has no lifestone and the first three portals are NoRecall), so their homes and stops are Holtburg's hand-set standing points.</summary>
    private static readonly string[] SpineKeys = ["tusker-burrow", "tusker-retreat", "tusker-lodge", "tusker-cave", "tusker-temple-20-40", "tusker-cavern", "tusker-habitat", "tusker-barracks", "tusker-abode", "tusker-quarters", "tusker-holding", "tusker-armory"]; // M6 3 p3: and the three alternates (Cavern, Habitat, Barracks); M6 14 armory: and the Armory; M6 retreat: and the Tusker Retreat (the 10-14 rung, hunted from Holtburg, D48)

    private static bool HoltburgHomed(string key) => IsDen(key) || SpineKeys.Contains(key) || key == AlabreeKey || key == CraterCavesKey || key == RedoubtKey;

    /// <summary>M6 8 B1: the Crater Caves are hunted from Holtburg (the climber's lifestone), though Crater Lake Village's Life Stone stands 495 m from the portal: the home is the hand-set standing point, as the spine's.</summary>
    private const string CraterCavesKey = "crater-caves";

    /// <summary>M6 5 q6b: the Cave of Alabree is hunted from Holtburg, whose own lifestone it is nearest: its home is the hand-set standing point, as the spine's.</summary>
    private const string AlabreeKey = "cave-of-alabree";

    /// <summary>M6 redoubt: the Holtburg Redoubt, homed at Holtburg by hand: its portal stands 458 m from Holtburg's lifestone and 352 m from the lifestone 0x7A9B0005 in landblock 0xA9B0, the draft's default.</summary>
    private const string RedoubtKey = "holtburg-redoubt";

    /// <summary>The draft of the ground's town half. M6 8 B1: the Crater Caves' arrival (0x019A02D6, the room the portal's point stands in) is not the row's stale Destination (0x019A02CA), which is what the draft's lookup asks for.</summary>
    private static DraftWorld DraftOf(HuntingGround ground, ushort? home) => PinnedWorldSql.Draft(Assert.Single(ground.Landblocks), ground.Key == CraterCavesKey ? 0x019A02CAu : ground.Arrival.Cell, home);

    /// <summary>The profile's own town: the draft's town half taking the lifestone of the profile's home landblock.</summary>
    private static DraftWorld OwnTownOf(HuntingGround ground) => DraftOf(ground, (ushort)(ground.Home.LifestoneCell >> 16));

    [RequiresWorldSqlDumpTheory]
    [MemberData(nameof(DenRoomGraphTests.Dungeons), MemberType = typeof(DenRoomGraphTests))]
    public void A_dungeon_s_hand_set_portal_home_and_vendors_equal_the_draft_s_town_half(string key)
    {
        var ground = GroundCatalog.Get(key);
        var world = OwnTownOf(ground);
        var town = world.Town;
        Assert.NotNull(town);
        output.WriteLine(string.Join("\n", world.Notes));

        // The entry portal: the weenie whose Destination is the profile's arrival; its instance, the cell by the outdoor-cell rule, the global tie and recall spells.
        Assert.Contains(world.Entries, e => e.Cell == (key == CraterCavesKey ? 0x019A02CAu : ground.Arrival.Cell));
        Assert.NotNull(town.Portal);
        if (ground.Portal is null)
        {
            Assert.False(ground.Entry!.Tieable); // a NoRecall entry portal has no tie record (M6 3 a1)
            Assert.Equal((ground.Entry.Guid, ground.Entry.Cell), (town.Portal.Guid, town.Portal.Cell));
        }
        else
        {
            Assert.Equal((ground.Portal.Guid, ground.Portal.Cell), (town.Portal.Guid, town.Portal.Cell));
            Assert.Equal(((uint)GroundDraft.PrimaryPortalTieSpell, (uint)GroundDraft.PrimaryPortalRecallSpell), (ground.Portal.TieSpell, ground.Portal.RecallSpell));
        }

        // The home: the lifestone's cell. The den's hand-set point is a standing point measured beside Holtburg's lifestone (session.md section 41), not the instance's own point 5 m away; Tusker Temple's is the instance's.
        var lifestone = town.Lifestone!.Place;
        Assert.Equal(ground.Home.LifestoneCell, lifestone.Cell);
        if (HoltburgHomed(key))
        {
            Assert.NotEqual(ground.Home.LifestonePoint, lifestone.Point);
            Assert.InRange(Vector3.Distance(ground.Home.LifestonePoint, lifestone.Point), 0f, 6f);
        }
        else
        {
            Assert.Equal(ground.Home.LifestonePoint, lifestone.Point);
        }

        // The vendors: every hand-set vendor is a draft vendor with the same wcid, name, cell, point and what it buys; every ware it sells is a shop row at the draft's price. The hand-set step chose which
        // vendors and wares (the draft lists the town's 8 or so, every ware); the den's Renald stops in his front room 0xA9B4013F, which the walk reaches, not in his own cell (docs/wayfinding.md), and Cindrue in her entry hall 0xA9B40117 (M5 3 fix F4, ResupplyRouteDatTests).
        foreach (var vendor in ground.Vendors)
        {
            var draft = Assert.Single(town.Vendors, v => v.Place.Guid == vendor.Guid);
            var learnOnly = vendor.BuysItemTypes == 0 && ground.Learn.Any(l => l.VendorWcid == vendor.Wcid); // M5 7 P7j: a learn list's vendor buys nothing here (no trip sells to her): the profile's mask is 0, not the draft's
            Assert.Equal((vendor.Wcid, vendor.Name, vendor.Cell, vendor.Point, learnOnly ? draft.BuysItemTypes : vendor.BuysItemTypes), (draft.Place.Wcid, draft.Name, draft.Place.Cell, draft.Place.Point, draft.BuysItemTypes));
            Assert.Equal(HoltburgHomed(key) && vendor.Wcid == 718 ? 0xA9B4013Fu : HoltburgHomed(key) && vendor.Wcid == 2304 ? 0xA9B40117u : vendor.Cell, vendor.StopCell);
            foreach (var ware in vendor.Sells)
            {
                var shop = Assert.Single(draft.Wares.Distinct(), w => w.Wcid == ware.Wcid); // M6 8 D: Fort Tethana's Yoshi Deshi lists each pack twice in his CreateList (the same row, the same price)
                var note = ground.Supplies.Purse.Notes.FirstOrDefault(n => n.Wcid == ware.Wcid);
                var price = note is null ? shop.Price : (int)Math.Ceiling((note.Value * (double)Lifestone.Client.Session.Contracts.VendorStock.PromissoryNoteSellRate) - 0.1); // M5 7 G1: a trade note is sold at ACE's 1.15 times its face, not the vendor's rate (Vendor.cs 577-585 @95ef426d)
                Assert.Equal((ware.Name, ware.Price), (shop.Name, price));
            }
        }

        output.WriteLine($"{key}: portal 0x{town.Portal.Guid:X8} in 0x{town.Portal.Cell:X8}, home 0x{lifestone.Cell:X8}, {ground.Vendors.Count} of the town's {town.Vendors.Count} vendors, all equal to the draft's");
    }

    [RequiresWorldSqlDumpTheory]
    [MemberData(nameof(DenRoomGraphTests.Dungeons), MemberType = typeof(DenRoomGraphTests))]
    public void The_draft_s_default_home_is_the_lifestone_nearest_the_portal_which_is_the_profile_s_but_for_the_den(string key)
    {
        var ground = GroundCatalog.Get(key);
        var world = DraftOf(ground, null);
        var home = world.Town!.Lifestone!;
        output.WriteLine($"{key}: the nearest lifestone 0x{home.Place.Guid:X8} in 0x{home.Place.Cell:X8} is {home.Metres:0} m from the portal; the profile's home is 0x{ground.Home.LifestoneCell:X8} ({ground.Home.Town})");
        if (HoltburgHomed(key))
        {
            // The den's hand-set home is Holtburg, where the ground's characters live and shop (the project's town since M4): the character recalls home, shops, and returns by Primary Portal Recall tied to the den's
            // portal. The den portal stands in landblock 0x1B90, 28 km from Holtburg; the lifestone nearest it, 2.9 km away, is another town's. The command's 'home' (a landblock) drafts Holtburg's town, as the theory above does.
            Assert.True(!IsDen(key) || ground.Home.LifestoneCell >> 16 != home.Place.Cell >> 16); // the spine grounds' nearest lifestone may be Holtburg's (the Temple 20-40's, 4.4 km)
            Assert.True(home.Metres > 1000 || key == AlabreeKey || key == CraterCavesKey || key == RedoubtKey, $"the lifestone nearest the den's portal is {home.Metres:0} m away"); // M6 5 q6b: the Cave of Alabree's portal stands 355 m from Holtburg's lifestone, which is its home
        }
        else
        {
            Assert.Equal(ground.Home.LifestoneCell, home.Place.Cell);
            Assert.Equal(ground.Home.LifestonePoint, home.Place.Point);
            Assert.Equal(ground.Vendors.Select(v => v.Guid).Order(), world.Town!.Vendors.Select(v => v.Place.Guid).Intersect(ground.Vendors.Select(v => v.Guid)).Order()); // the nearest town's vendors include the profile's
        }
    }

    [RequiresWorldSqlDumpTheory]
    [MemberData(nameof(DenRoomGraphTests.Dungeons), MemberType = typeof(DenRoomGraphTests))]
    public void A_creature_whose_emote_teleports_is_avoided_and_stands_in_no_go_cells_and_the_level_band_is_the_targets(string key)
    {
        var ground = GroundCatalog.Get(key);
        var world = DraftOf(ground, null);

        // Exits by emote: avoided in the profile, every slot cell no-go. Tusker Temple's Shrine Statue (wcid 22641): its Use emote casts spell 2936 'Entrance to Tusker Island' (the research took it for a Death emote;
        // ACE's EmoteCategory 7 is Use, 3 is Death: using the statue takes the player out). The den has none.
        Assert.All(world.EmoteExits, e =>
        {
            Assert.Equal(GroundRole.Avoid, ground.RoleOf(e.Wcid));
            Assert.All(e.Cells, c => Assert.True(ground.IsNoGo(c), $"{key}: the exit creature {e.Wcid}'s cell 0x{c:X8} is no-go"));
        });
        if (key is DenKey or ShoDenKey or "tusker-burrow" or "tusker-retreat" or "tusker-lodge" or "tusker-cave" or "tusker-cavern" or "tusker-habitat" or "tusker-barracks" or "tusker-abode" or "tusker-quarters" or "tusker-holding" or "tusker-armory" or AlabreeKey or CraterCavesKey or RedoubtKey)
        {
            Assert.Empty(world.EmoteExits);
        }
        else
        {
            var statue = Assert.Single(world.EmoteExits);
            Assert.Equal((22641u, "Use", 2936u, "Entrance to Tusker Island", key == "tusker-temple-20-40" ? "0x5D43017F" : "0x5C4A017F"), (statue.Wcid, statue.CategoryName, statue.Spell, statue.SpellName, string.Join(",", statue.Cells.Select(c => $"0x{c:X8}"))));
        }

        // The level band is the targets' levels only (not every slot's creature), inside the slot creatures' range.
        var targets = ground.Targets.Where(t => t.Role == GroundRole.Target).Select(t => t.Level).ToArray();
        Assert.Equal(ground.LevelBand, GroundDraft.LevelBandOf(targets));
        var slotLevels = world.Creatures.Where(c => c.Level is not null).Select(c => c.Level!.Value).Concat(key == AlabreeKey ? [8, 15] : []).ToArray(); // M6 5 q6b: the placed Blood Shreth (8) and Hunter Shreth (15) the draft drops (TODO(ground-draft-link-generator))
        Assert.InRange(ground.LevelBand.Min, slotLevels.Min(), slotLevels.Max());
        Assert.InRange(ground.LevelBand.Max, slotLevels.Min(), slotLevels.Max());

        // The proposed roles: every profile target is a proposed target. Tusker Temple's roles are the proposal's; the den's avoided classes (the Virindi and the Drudges) are its design's, set by hand.
        Assert.All(ground.Targets.Where(t => t.Role == GroundRole.Target && !(key == AlabreeKey && t.Wcid is 4110 or 4111)), t => Assert.Equal(GroundRole.Target, GroundDraft.ProposedRole(world, t.Wcid)));
        var proposed = GroundDraft.LevelBandOf(world.Creatures.Where(c => c.Level is not null && GroundDraft.ProposedRole(world, c.Wcid) == GroundRole.Target).Select(c => c.Level!.Value));
        if (IsDen(key))
        {
            Assert.Equal(new LevelBand(40, 80), proposed);
            Assert.Equal(new LevelBand(80, 80), ground.LevelBand);
        }
        else
        {
            Assert.Equal(ground.Targets.Where(t => key != AlabreeKey || t.Wcid is not (4110 or 4111)).Select(t => (t.Wcid, t.Role)).Order(), world.Creatures.Select(c => (c.Wcid, GroundDraft.ProposedRole(world, c.Wcid))).Order());
            Assert.Equal(key == AlabreeKey ? new LevelBand(7, 11) : ground.LevelBand, proposed); // M6 5 q6b: the draft's band lacks the placed Shreths (8, 15), TODO(ground-draft-link-generator)
        }
    }

    [RequiresWorldSqlDumpTheory]
    [MemberData(nameof(DenRoomGraphTests.Dungeons), MemberType = typeof(DenRoomGraphTests))]
    public void The_draft_json_carries_the_arrival_the_portal_the_home_and_the_vendors_with_no_arrival_named(string key)
    {
        var ground = GroundCatalog.Get(key);
        var world = OwnTownOf(ground);
        var landblock = Assert.Single(ground.Landblocks);
        var all = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF)));
        var cells = GroundDraft.Cells(all, world.Arrival!.Cell, ground.Walk.DoorwayLean);
        using var json = JsonDocument.Parse(GroundDraft.Json("draft", all, cells, world));
        var root = json.RootElement;
        Assert.Equal($"0x{(key == CraterCavesKey ? 0x019A02CAu : ground.Arrival.Cell):X8}", root.GetProperty("arrival").GetProperty("cell").GetString()); // M6 8 B1: the Crater Caves name the room the point stands in (DraftOf)
        if (ground.Portal is { } tie) // a NoRecall entry has no tie record to compare
        {
            Assert.Equal($"0x{tie.Guid:X8}", root.GetProperty("portal").GetProperty("guid").GetString());
            Assert.Equal($"0x{tie.Cell:X8}", root.GetProperty("portal").GetProperty("cell").GetString());
        }
        Assert.Equal($"0x{ground.Home.LifestoneCell:X8}", root.GetProperty("home").GetProperty("lifestone_cell").GetString());
        Assert.StartsWith(GroundDraft.HandSet, root.GetProperty("home").GetProperty("town").GetString(), StringComparison.Ordinal); // the town's name is by hand
        var drafted = root.GetProperty("vendors").EnumerateArray().Select(v => v.GetProperty("guid").GetString()).ToArray();
        Assert.All(ground.Vendors, v => Assert.Contains($"0x{v.Guid:X8}", drafted));
        Assert.StartsWith(GroundDraft.HandSet, root.GetProperty("sources").GetProperty("vendors").GetString(), StringComparison.Ordinal); // which vendors and wares a ground uses is by hand
        Assert.StartsWith(GroundDraft.HandSet, root.GetProperty("walk").GetProperty("stop_within_m").GetString(), StringComparison.Ordinal); // so a draft never loads by accident
        output.WriteLine($"{key}: the draft json carries the portal, the home and {drafted.Length} vendors");
    }

    [RequiresWorldSqlDumpFact]
    public void The_temple_s_entry_exit_and_target_stats_are_the_pinned_sql_s_M6_3_a1()
    {
        const string temple = "tusker-temple-fort-tethana";
        var ground = GroundCatalog.Get(temple);
        var world = OwnTownOf(ground);
        output.WriteLine(string.Join("\n", world.Notes));

        // The entry: portal 22696's gate (MinLevel 40, no MaxLevel) and its PortalBitmask 1 (Unrestricted, no NoRecall), so it can be tied; the place and front point are the draft's.
        var entry = Assert.Single(world.Entries, e => e.Wcid == 22696);
        Assert.Equal(((int?)40, (int?)null, (int?)1, true), (entry.MinLevel, entry.MaxLevel, entry.PortalBitmask, entry.Tieable));
        var portal = world.Town!.Portal!;
        var (frontCell, front) = GroundDraft.FrontOf(portal);
        front = new Vector3(MathF.Round(front.X, 4), MathF.Round(front.Y, 4), MathF.Round(front.Z, 4)); // the draft writes four places
        Assert.Equal(new GroundEntry(entry.Wcid, portal.Guid, portal.Cell, portal.Point, frontCell, front, entry.MinLevel, entry.MaxLevel, entry.Tieable), ground.Entry);
        Assert.Equal((ground.Portal!.FrontCell, ground.Portal.Front), (frontCell, front)); // the hand-derived tie point is the draft's rule

        // The exit: no exit portal in 0x5C4A (the statue's Use emote is avoided), so the ground is left by recall.
        Assert.Empty(world.Exits);
        Assert.Equal(GroundExitKind.Recall, ground.Exit!.Kind);

        // The stats: every profile target's equal the draft's; the Tusker Slave's by value.
        var slave = world.Creatures.Single(c => c.Wcid == 1628);
        Assert.Equal(((int?)400, (int?)219, (uint?)4u, new GroundAttributes(210, 300, 200, 180, 70, 60)), (slave.MaxHealth, slave.MagicDefense, slave.DamageTypes, slave.Attributes));
        foreach (var t in ground.Targets)
        {
            var c = world.Creatures.Single(x => x.Wcid == t.Wcid);
            Assert.Equal((c.MaxHealth ?? 0, c.MagicDefense ?? 0, c.DamageTypes ?? 0u, c.Attributes), (t.MaxHealth, t.MagicDefence, t.DamageTypes, t.Attributes));
        }
    }

    [RequiresWorldSqlDumpFact]
    public void The_den_s_health_measures_against_ace_s_max_value_M6_3_a1()
    {
        // M6 3 a2: the den's profile carries ACE's current values (the Guard's row says current_Level 260, ACE spawns it at MaxValue 400: Creature.cs:132-135 @95ef426d), so the profile and the draft agree on health and Magic Defense.
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        var world = DraftOf(den, (ushort)(den.Home.LifestoneCell >> 16));
        foreach (var wcid in new uint[] { 1628, 1629 })
        {
            var profile = den.Targets.Single(t => t.Wcid == wcid);
            var drafted = world.Creatures.Single(c => c.Wcid == wcid);
            Assert.Equal(((int?)profile.MaxHealth, (int?)profile.MagicDefence), (drafted.MaxHealth, drafted.MagicDefense));
        }

        Assert.Equal((400, 244), (den.Targets.Single(t => t.Wcid == 1629).MaxHealth, den.Targets.Single(t => t.Wcid == 1629).MagicDefence));
    }

    [RequiresDatsFact]
    public void The_draft_s_magic_defense_formula_is_the_skill_table_s_M6_3_a1()
    {
        var skill = Lifestone.Data.CharacterCreation.SkillTableData.Load(fixture.Dats).Skills.Single(s => s.Id == GroundDraft.MagicDefenseSkill);
        Assert.Equal("Magic Defense", skill.Name);
        Assert.Equal(GroundDraft.MagicDefenseFormula, skill.Formula);
    }

    [RequiresDatsFact]
    public void Spell_2936_the_statue_casts_is_a_portal_sending_spell_in_the_dats()
    {
        var spell = Lifestone.Data.Magic.SpellTableData.Load(fixture.Dats).Spells[2936];
        Assert.Equal("Entrance to Tusker Island", spell.Name);
        Assert.Contains("Portal", spell.MetaSpellType.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>M6 3 a1: a fact that needs the dats and the pinned world SQL dump (the theory's rule, <see cref="RequiresWorldSqlDumpTheoryAttribute"/>): skipped, with the reason, otherwise.</summary>
internal sealed class RequiresWorldSqlDumpFactAttribute : FactAttribute
{
    public RequiresWorldSqlDumpFactAttribute()
    {
        if (!Lifestone.Data.Dat.DatSet.Exists())
        {
            Skip = "needs the retail dats";
        }
        else if (PinnedWorldSql.Zip() is null)
        {
            Skip = "needs the pinned world SQL dump under server/runtime/downloads of the main checkout";
        }
    }
}
