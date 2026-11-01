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

    private static DraftWorld DraftOf(HuntingGround ground, ushort? home) => PinnedWorldSql.Draft(Assert.Single(ground.Landblocks), ground.Arrival.Cell, home);

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
        Assert.Contains(world.Entries, e => e.Cell == ground.Arrival.Cell);
        Assert.NotNull(ground.Portal);
        Assert.NotNull(town.Portal);
        Assert.Equal((ground.Portal.Guid, ground.Portal.Cell), (town.Portal.Guid, town.Portal.Cell));
        Assert.Equal(((uint)GroundDraft.PrimaryPortalTieSpell, (uint)GroundDraft.PrimaryPortalRecallSpell), (ground.Portal.TieSpell, ground.Portal.RecallSpell));

        // The home: the lifestone's cell. The den's hand-set point is a standing point measured beside Holtburg's lifestone (session.md section 41), not the instance's own point 5 m away; Tusker Temple's is the instance's.
        var lifestone = town.Lifestone!.Place;
        Assert.Equal(ground.Home.LifestoneCell, lifestone.Cell);
        if (key == DenKey)
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
            Assert.Equal(key == DenKey && vendor.Wcid == 718 ? 0xA9B4013Fu : key == DenKey && vendor.Wcid == 2304 ? 0xA9B40117u : vendor.Cell, vendor.StopCell);
            foreach (var ware in vendor.Sells)
            {
                var shop = Assert.Single(draft.Wares, w => w.Wcid == ware.Wcid);
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
        if (key == DenKey)
        {
            // The den's hand-set home is Holtburg, where the ground's characters live and shop (the project's town since M4): the character recalls home, shops, and returns by Primary Portal Recall tied to the den's
            // portal. The den portal stands in landblock 0x1B90, 28 km from Holtburg; the lifestone nearest it, 2.9 km away, is another town's. The command's 'home' (a landblock) drafts Holtburg's town, as the theory above does.
            Assert.NotEqual(ground.Home.LifestoneCell >> 16, home.Place.Cell >> 16);
            Assert.True(home.Metres > 1000, $"the lifestone nearest the den's portal is {home.Metres:0} m away");
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
        if (key == DenKey)
        {
            Assert.Empty(world.EmoteExits);
        }
        else
        {
            var statue = Assert.Single(world.EmoteExits);
            Assert.Equal((22641u, "Use", 2936u, "Entrance to Tusker Island", "0x5C4A017F"), (statue.Wcid, statue.CategoryName, statue.Spell, statue.SpellName, string.Join(",", statue.Cells.Select(c => $"0x{c:X8}"))));
        }

        // The level band is the targets' levels only (not every slot's creature), inside the slot creatures' range.
        var targets = ground.Targets.Where(t => t.Role == GroundRole.Target).Select(t => t.Level).ToArray();
        Assert.Equal(ground.LevelBand, GroundDraft.LevelBandOf(targets));
        var slotLevels = world.Creatures.Where(c => c.Level is not null).Select(c => c.Level!.Value).ToArray();
        Assert.InRange(ground.LevelBand.Min, slotLevels.Min(), slotLevels.Max());
        Assert.InRange(ground.LevelBand.Max, slotLevels.Min(), slotLevels.Max());

        // The proposed roles: every profile target is a proposed target. Tusker Temple's roles are the proposal's; the den's avoided classes (the Virindi and the Drudges) are its design's, set by hand.
        Assert.All(ground.Targets.Where(t => t.Role == GroundRole.Target), t => Assert.Equal(GroundRole.Target, GroundDraft.ProposedRole(world, t.Wcid)));
        var proposed = GroundDraft.LevelBandOf(world.Creatures.Where(c => c.Level is not null && GroundDraft.ProposedRole(world, c.Wcid) == GroundRole.Target).Select(c => c.Level!.Value));
        if (key == DenKey)
        {
            Assert.Equal(new LevelBand(40, 80), proposed);
            Assert.Equal(new LevelBand(80, 80), ground.LevelBand);
        }
        else
        {
            Assert.Equal(ground.Targets.Select(t => (t.Wcid, t.Role)).Order(), world.Creatures.Select(c => (c.Wcid, GroundDraft.ProposedRole(world, c.Wcid))).Order());
            Assert.Equal(ground.LevelBand, proposed);
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
        Assert.Equal($"0x{ground.Arrival.Cell:X8}", root.GetProperty("arrival").GetProperty("cell").GetString());
        Assert.Equal($"0x{ground.Portal!.Guid:X8}", root.GetProperty("portal").GetProperty("guid").GetString());
        Assert.Equal($"0x{ground.Portal.Cell:X8}", root.GetProperty("portal").GetProperty("cell").GetString());
        Assert.Equal($"0x{ground.Home.LifestoneCell:X8}", root.GetProperty("home").GetProperty("lifestone_cell").GetString());
        Assert.StartsWith(GroundDraft.HandSet, root.GetProperty("home").GetProperty("town").GetString(), StringComparison.Ordinal); // the town's name is by hand
        var drafted = root.GetProperty("vendors").EnumerateArray().Select(v => v.GetProperty("guid").GetString()).ToArray();
        Assert.All(ground.Vendors, v => Assert.Contains($"0x{v.Guid:X8}", drafted));
        Assert.StartsWith(GroundDraft.HandSet, root.GetProperty("sources").GetProperty("vendors").GetString(), StringComparison.Ordinal); // which vendors and wares a ground uses is by hand
        Assert.StartsWith(GroundDraft.HandSet, root.GetProperty("walk").GetProperty("stop_within_m").GetString(), StringComparison.Ordinal); // so a draft never loads by accident
        output.WriteLine($"{key}: the draft json carries the portal, the home and {drafted.Length} vendors");
    }

    [RequiresDatsFact]
    public void Spell_2936_the_statue_casts_is_a_portal_sending_spell_in_the_dats()
    {
        var spell = Lifestone.Data.Magic.SpellTableData.Load(fixture.Dats).Spells[2936];
        Assert.Equal("Entrance to Tusker Island", spell.Name);
        Assert.Contains("Portal", spell.MetaSpellType.ToString(), StringComparison.Ordinal);
    }
}
