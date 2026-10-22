using System.Globalization;
using System.Numerics;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Session.Fighter;

/// <summary>What the dats say of a hunt cell's ground (<c>HuntGroundDatTests</c> pins it; M4 1.2 s4a, <c>TODO(m4-belt-cell-terrain)</c>).</summary>
public enum HuntTerrain
{
    /// <summary>Not read: the hunt does not use the cell.</summary>
    Unread,

    /// <summary>No water at the cell's four corners and every sampled surface normal is walkable.</summary>
    Dry,

    /// <summary>A corner of the cell is water (terrain types 16-20, ACE's table, <c>LandblockStruct.cs:62</c>).</summary>
    Water,

    /// <summary>A sampled surface normal is under <c>PhysicsGlobals.FloorZ</c> (the physics floor: 0.664, about 48 degrees): the walker cannot stand there.</summary>
    Steep,
}

/// <summary>
/// One 24 m outdoor cell of a hunt ground: its cell id, the map coordinates the world database gave it and its distance from Holtburg in map units. The cell's centre is the point the hunt
/// teleports to (<c>place loc</c>), at the terrain height plus 0.5 m.
/// </summary>
/// <param name="CellId">The outdoor cell id (<c>0xBBBB00II</c>, <c>II</c> 1 to 64).</param>
/// <param name="North">Map north of the cell's centre.</param>
/// <param name="East">Map east of the cell's centre.</param>
/// <param name="MapUnits">The distance from Holtburg's centre (42.5N 33.6E) in map units.</param>
/// <param name="Terrain">What the dats say (<see cref="HuntTerrain"/>).</param>
public sealed record HuntCell(uint CellId, double North, double East, double MapUnits, HuntTerrain Terrain)
{
    /// <summary>The cell's x within its landblock: <c>(i - 1) / 8</c> cells of 24 m, then the middle (<c>x * 24 + 12</c>).</summary>
    public float X => (((int)(CellId & 0xFFFF) - 1) / 8 * 24) + 12;

    /// <summary>The cell's y within its landblock: <c>(i - 1) % 8</c> cells of 24 m, then the middle.</summary>
    public float Y => (((int)(CellId & 0xFFFF) - 1) % 8 * 24) + 12;

    /// <summary>True for the cells the hunt goes to: dry and walkable.</summary>
    public bool Usable => Terrain == HuntTerrain.Dry;

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"0x{CellId:X8} ({North:0.00}N {East:0.00}E, {MapUnits:0.00} units, {Terrain})");
}

/// <summary>
/// A hunt ground as data (spec 5.9): a name, the level band (a filter: the hunt attacks only a creature whose level is in it), the creature names (for the logs), the cells to stand in and, for a used row, the
/// <see cref="Table"/> of its creatures by wcid and level. The creature level is not on the wire (<c>TODO(m4-creature-level)</c>), so the hunt reads it from the table, never from the object.
/// <see cref="InUse"/> is false for a row no scenario goes to yet.
/// </summary>
public sealed record HuntGroundRow(string Name, int MinLevel, int MaxLevel, IReadOnlyList<string> Creatures, IReadOnlyList<HuntCell> Cells, bool InUse)
{
    /// <summary>The cells the hunt uses, in the table's order: dry and walkable.</summary>
    public IEnumerable<HuntCell> UsableCells => Cells.Where(c => c.Usable);

    /// <summary>The creatures the row's generators spawn, by weenie class id with their level (empty for a row that is data only).</summary>
    public IReadOnlyList<HuntCreature> Table { get; init; } = [];

    /// <summary>The level of a creature of this weenie class in <see cref="Table"/>, or null when it is not listed (never a target).</summary>
    public int? LevelOf(uint wcid)
    {
        foreach (var c in Table)
        {
            if (c.Wcid == wcid)
            {
                return c.Level;
            }
        }

        return null;
    }

    /// <summary>The number of table creatures whose level is in the band (<see cref="MinLevel"/> to <see cref="MaxLevel"/>).</summary>
    public int InBandCount => Table.Count(c => c.Level >= MinLevel && c.Level <= MaxLevel);
}

/// <summary>One creature of a hunt ground's table: its weenie class id, its name (for the logs) and its level (int property 25 of the weenie, from the pinned release's database).</summary>
/// <param name="Wcid">The weenie class id (<c>CreateObject</c>'s <c>WeenieClassId</c>).</param>
/// <param name="Name">The name, for the logs.</param>
/// <param name="Level">The creature's level.</param>
public sealed record HuntCreature(uint Wcid, string Name, int Level);

/// <summary>
/// Holtburg's hunt grounds (docs/milestones/m4-fights-and-loot.md 5.9, read with SELECTs from the pinned release v0.9.296 by 1.1's lead: m4-plan.md 8 item 1.1).
/// </summary>
public static class HuntGround
{
    /// <summary>The log category of the hunt's lines.</summary>
    public const string LogCategory = "hunt";

    /// <summary>
    /// Every creature (ItemType 16) that the Low B belt's generators 1999 and 2001 spawn, three generator levels down, with its Level (int property 25): a read-only SELECT on the pinned release v0.9.296
    /// (the lead's, M4 1.2 fix slice, review 1.2-R1; 37 rows, levels 4 to 30). Keyed by <b>weenie class id</b> (the CreateObject carries it; names are not unique per level); the name is kept for the logs.
    /// The hunt's target is the nearest creature whose wcid is in this table and whose level is in the row's band: the Tumerok Warrior (233, level 30, through the Drudge Low Slave Camp) stays listed and the band
    /// filters it out, and a creature whose wcid is not listed is never a target.
    /// </summary>
    public static IReadOnlyList<HuntCreature> LowBTable { get; } =
    [
        new(182, "Auroch Yearling", 4),
        new(26684, "Blood Shrethlet", 7),
        new(6, "Banderling Scout", 8),
        new(4110, "Blood Shreth", 8),
        new(215, "Blue Phyntos Wasp", 8),
        new(192, "Drudge Prowler", 8),
        new(1464, "Drudge Robber", 8),
        new(7, "Drudge Skulker", 8),
        new(193, "Drudge Slinker", 8),
        new(940, "Drudge Sneaker", 8),
        new(7990, "Field Ursuin", 8),
        new(948, "Mosswart Feeder", 8),
        new(2612, "Outcast Monouga", 8),
        new(12017, "Red Phyntos Drone", 8),
        new(12, "Red Phyntos Wasp", 8),
        new(223, "Reedshark Pup", 8),
        new(6535, "Small Shadow Child", 8),
        new(20, "Auroch Bull", 9),
        new(181, "Auroch Cow", 9),
        new(26689, "Field Ursuin Cub", 9),
        new(26687, "Hunter Shrethlet", 11),
        new(221, "Adult Reedshark", 15),
        new(937, "Banderling Guard", 15),
        new(947, "Barker Mosswart", 15),
        new(1631, "Drudge Servant", 15),
        new(1632, "Drudge Slave", 15),
        new(11989, "Drudge Vagabond", 15),
        new(4111, "Hunter Shreth", 15),
        new(1622, "Mire Phyntos Wasp", 15),
        new(211, "Mudlurk Mosswart", 15),
        new(12004, "Outcast Lord", 15),
        new(18, "Reedshark Elder", 15),
        new(222, "Veteran Reedshark", 15),
        new(11537, "Virtuous Doll", 15),
        new(8010, "Broken Fragment", 20),
        new(946, "Mite Squire", 20),
        new(233, "Tumerok Warrior", 30),
    ];

    /// <summary>The distinct names of <see cref="LowBTable"/>, in the table's order (the logs and the "names were not found" line).</summary>
    public static IReadOnlyList<string> LowBCreatures { get; } = [.. LowBTable.Select(c => c.Name).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The Low B belt: generators 1999 and 2001, a ring 4.5 to 8 map units around Holtburg (42.5N 33.6E); ACE rolls children by cumulative probability, so the band is 8 to 20 (the 1.1 SELECT's correction of spec 5.9). The ten cells are the
    /// south-west ones nearest the town; their terrain is the dat test's finding (<c>HuntGroundDatTests</c>).
    /// </summary>
    public static HuntGroundRow LowB { get; } = new(
        "Low B belt",
        8,
        20,
        LowBCreatures,
        [
            new(0xA5B00032, 38.95, 30.65, 4.62, HuntTerrain.Dry),
            new(0xA4B0003F, 39.45, 29.95, 4.76, HuntTerrain.Dry),
            new(0xA5B0000C, 39.15, 30.15, 4.81, HuntTerrain.Dry),
            new(0xA4B0003E, 39.35, 29.95, 4.82, HuntTerrain.Dry),
            new(0xA6AF000D, 38.45, 30.95, 4.84, HuntTerrain.Dry),
            new(0xA4B1000E, 40.15, 29.35, 4.86, HuntTerrain.Dry),
            new(0xA6AF000C, 38.35, 30.95, 4.92, HuntTerrain.Dry),
            new(0xA5AF003D, 38.45, 30.75, 4.95, HuntTerrain.Dry),
            new(0xA4B10012, 39.75, 29.45, 4.98, HuntTerrain.Dry),
            new(0xA3B1003F, 40.25, 29.15, 4.99, HuntTerrain.Dry),
        ],
        InUse: true)
    {
        Table = LowBTable,
    };

    /// <summary>
    /// Every creature (ItemType 16) that the Low North Forest Mix's generator 21183 spawns, six generator levels down, with its Level (int property 25): the lead's read-only SELECT on the pinned release
    /// v0.9.296 (M4 3.1, <c>cache/m4/3-1/forest-creatures.tsv</c>, 40 rows, levels 3 to 171). Keyed by <b>weenie class id</b> (the CreateObject carries it); the name is kept for the logs. In the table's order: level, then
    /// wcid. The two rows that are never a target by kind stay out: Fisana the Jeweler (411, an NPC, level 3) and the Tusker Shrine Statue (22641, level 171). The other 38 stay listed and the band
    /// (30 to 40) keeps the rest honest. The level is not on the wire (<c>TODO(m4-creature-level)</c>), so this table is its source; a creature whose wcid is not listed is never a target.
    /// </summary>
    public static IReadOnlyList<HuntCreature> LowNorthForestTable { get; } =
    [
        new(2612, "Outcast Monouga", 8),
        new(1756, "Shadow Child", 15),
        new(1760, "Skeleton Warrior", 20),
        new(1761, "Skeleton Captain", 20),
        new(2574, "Rough Monouga", 20),
        new(11528, "Elaniwood Golem", 20),
        new(213, "Olthoi Nymph", 30),
        new(235, "Goldenback Tusker", 30),
        new(1627, "Tusker Crimsonback", 30),
        new(1758, "Shadow", 30),
        new(2575, "Crude Monouga", 30),
        new(2576, "Wild Monouga", 30),
        new(194, "Copper Golem", 40),
        new(1608, "Drudge Lurker", 40),
        new(1762, "Skeleton Lord", 40),
        new(22009, "Olthoi Drone", 40),
        new(24942, "Gotrok Lithos", 40),
        new(27254, "Copper Golem Kingpin", 40),
        new(28877, "Nasty Scarecrow", 40),
        new(3, "Olthoi Worker", 50),
        new(217, "Gold Phyntos Wasp", 50),
        new(1609, "Drudge Stalker", 50),
        new(1989, "Shadow Wisp", 50),
        new(7128, "Sufut Zefir", 50),
        new(7978, "Scrawed Grievver", 50),
        new(7979, "Virulent Grievver", 50),
        new(9251, "Brutish Monouga", 50),
        new(22010, "Olthoi Servant", 50),
        new(24940, "Gotrok Gigas", 50),
        new(24959, "Olthoi Needler", 50),
        new(28878, "Naughty Scarecrow", 50),
        new(28879, "Nefarious Scarecrow", 50),
        new(1630, "Lich Lord", 60),
        new(1757, "Shadow Lieutenant", 60),
        new(9253, "Wily Monouga", 60),
        new(34102, "Old Zombie", 60),
        new(1610, "Drudge Ravener", 80),
        new(4253, "Panumbris Shadow", 80),
    ];

    /// <summary>The distinct names of <see cref="LowNorthForestTable"/>, in the table's order (the logs and the "names were not found" line).</summary>
    public static IReadOnlyList<string> LowNorthForestCreatures { get; } = [.. LowNorthForestTable.Select(c => c.Name).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The Low North Forest Mix (spec 5.9 item 2; generator 21183, the 0x9FB* landblocks 7.7 to 9.2 map units west of Holtburg): the forest ground, chosen with <c>--hunt-ground forest</c> (the belt stays the default). The band
    /// is <b>30 to 40</b>: the hunter is level 40, so the lead narrowed the spec's 30 to 60 (m4-plan.md 4.6; thirteen of the table's creatures are in it). The cells are the 0x9FB* strip of <c>forest-candidates.tsv</c>, nearest first;
    /// ACE rolls a spot's children by cumulative probability, so a cell may hold nothing and the hunt tries the next one, as on the belt. Their terrain is the dat test's finding (<c>HuntGroundDatTests</c>, 2026-10-21: all
    /// 33 read dry and flat, lowest normal z 0.973 or more; closes <c>TODO(m4-forest-cells)</c>).
    /// </summary>
    public static HuntGroundRow LowNorthForestMix { get; } = new(
        "Low North Forest Mix",
        30,
        40,
        LowNorthForestCreatures,
        [
            new(0x9FB50039, 42.85, 25.95, 7.66, HuntTerrain.Dry),
            new(0x9FB30039, 41.25, 25.95, 7.75, HuntTerrain.Dry),
            new(0x9FB60038, 44.35, 25.85, 7.97, HuntTerrain.Dry),
            new(0x9FB10038, 40.35, 25.85, 8.04, HuntTerrain.Dry),
            new(0x9FB6002F, 44.25, 25.75, 8.04, HuntTerrain.Dry),
            new(0x9FB30021, 41.25, 25.65, 8.05, HuntTerrain.Dry),
            new(0x9FB60030, 44.35, 25.75, 8.07, HuntTerrain.Dry),
            new(0x9FB5001C, 43.15, 25.55, 8.08, HuntTerrain.Dry),
            new(0x9FB20025, 40.85, 25.65, 8.12, HuntTerrain.Dry),
            new(0x9FB60019, 43.65, 25.55, 8.13, HuntTerrain.Dry),
            new(0x9FB70036, 44.95, 25.85, 8.13, HuntTerrain.Dry),
            new(0x9FB40012, 42.15, 25.45, 8.16, HuntTerrain.Dry),
            new(0x9FB50011, 42.85, 25.45, 8.16, HuntTerrain.Dry),
            new(0x9FB70037, 45.05, 25.85, 8.16, HuntTerrain.Dry),
            new(0x9FB50016, 43.35, 25.45, 8.19, HuntTerrain.Dry),
            new(0x9FB2001E, 40.95, 25.55, 8.20, HuntTerrain.Dry),
            new(0x9FB8003B, 45.45, 25.95, 8.20, HuntTerrain.Dry),
            new(0x9FB70022, 44.55, 25.65, 8.21, HuntTerrain.Dry),
            new(0x9FB70024, 44.75, 25.65, 8.26, HuntTerrain.Dry),
            new(0x9FB8002A, 45.35, 25.75, 8.35, HuntTerrain.Dry),
            new(0x9FB20013, 40.65, 25.45, 8.36, HuntTerrain.Dry),
            new(0x9FB00036, 39.35, 25.85, 8.37, HuntTerrain.Dry),
            new(0x9FB6000C, 43.95, 25.35, 8.38, HuntTerrain.Dry),
            new(0x9FB7001E, 44.95, 25.55, 8.41, HuntTerrain.Dry),
            new(0x9FB2000C, 40.75, 25.35, 8.43, HuntTerrain.Dry),
            new(0x9FB70014, 44.75, 25.45, 8.45, HuntTerrain.Dry),
            new(0x9FB20009, 40.45, 25.35, 8.50, HuntTerrain.Dry),
            new(0x9FB10015, 40.05, 25.45, 8.51, HuntTerrain.Dry),
            new(0x9FB7000B, 44.65, 25.35, 8.53, HuntTerrain.Dry),
            new(0x9FB10014, 39.95, 25.45, 8.54, HuntTerrain.Dry),
            new(0x9FB1000F, 40.25, 25.35, 8.55, HuntTerrain.Dry),
            new(0x9FB60008, 44.35, 25.25, 8.55, HuntTerrain.Dry),
            new(0x9FB70018, 45.15, 25.45, 8.57, HuntTerrain.Dry),
        ],
        InUse: true)
    {
        Table = LowNorthForestTable,
    };

    /// <summary>The rows, the used one first.</summary>
    public static IReadOnlyList<HuntGroundRow> Rows { get; } = [LowB, LowNorthForestMix];

    /// <summary>
    /// The height at the cell's centre, from the dats (<see cref="TerrainSampler.GetHeight"/> over the landblock's terrain), or null when the landblock has no terrain. The hunt stands the player
    /// 0.5 m above it (the server drops it to the floor).
    /// </summary>
    public static float? TerrainHeight(DatSet dats, HuntCell cell)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(cell);
        var terrain = dats.GetLandblockTerrain(LandblockCoord.FromId(cell.CellId));
        return terrain is null ? null : new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable).GetHeight(cell.X, cell.Y);
    }

    /// <summary>The point <c>place loc</c> sends the hunt to: the cell's centre at the terrain height plus 0.5 m.</summary>
    public static Vector3 PlacePoint(HuntCell cell, float terrainHeight) => new(cell.X, cell.Y, terrainHeight + 0.5f);
}
