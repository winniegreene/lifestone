using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Grounds;
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
/// One 24 m outdoor cell of a hunt ground: its cell id, the map coordinates the world database gave it and its distance from Holtburg in map units. The hunt goes to the cell's <see cref="Spot"/> (the
/// cell's centre unless the profile gives the encounter spot's point, M5 item 2 g3), at the terrain height plus 0.5 m.
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

    /// <summary>
    /// The landblock-local (x, y) the profile says the hunt stops at in this cell, or null for the cell's centre (<see cref="X"/>, <see cref="Y"/>). The belt stops at its cells' centres; the forest at each
    /// encounter spot's point, the generator's corner (cell x * 24, cell y * 24), 17 m from the centre (M5 D42).
    /// </summary>
    public Vector2? Spot { get; init; }

    /// <summary>The x the hunt goes to: the profile's spot, else the cell's centre.</summary>
    public float StopX => Spot?.X ?? X;

    /// <summary>The y the hunt goes to: the profile's spot, else the cell's centre.</summary>
    public float StopY => Spot?.Y ?? Y;

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

    /// <summary>
    /// The most cells a hunt on foot visits on this row, from the profile's <c>cell_cap</c> (M5 item 2 g3, D42): the belt's 8, the forest's every spot (33). Null for a row built by hand: the hunt's own
    /// <c>OnFootMaxCells</c> applies.
    /// </summary>
    public int? CellCap { get; init; }

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

    /// <summary>
    /// The expected usable creatures a spot of this row: the sum of the spawn masses of the <see cref="Table"/> creatures in the band (<see cref="MinLevel"/> to <see cref="MaxLevel"/>). The hunt narrows the band and
    /// the table to a fighter's own before it asks (M5 D42: 0.167 for the archer's 20-30 band over the forest, about 0.34 for the row's own 30-40).
    /// </summary>
    public double ExpectedYield() => Table.Where(c => c.Level >= MinLevel && c.Level <= MaxLevel).Sum(c => c.SpawnMass);
}

/// <summary>One creature of a hunt ground's table: its weenie class id, its name (for the logs) and its level (int property 25 of the weenie, from the pinned release's database).</summary>
/// <param name="Wcid">The weenie class id (<c>CreateObject</c>'s <c>WeenieClassId</c>).</param>
/// <param name="Name">The name, for the logs.</param>
/// <param name="Level">The creature's level.</param>
public sealed record HuntCreature(uint Wcid, string Name, int Level)
{
    /// <summary>The chance, 0 to 1, that one roll of a spot's generator spawns this creature (the profile's <c>spawn_mass</c>; 0 for a creature built by hand).</summary>
    public double SpawnMass { get; init; }
}

/// <summary>
/// Holtburg's hunt grounds (docs/milestones/m4-fights-and-loot.md 5.9, read with SELECTs from the pinned release v0.9.296 by 1.1's lead: m4-plan.md 8 item 1.1).
/// </summary>
public static class HuntGround
{
    /// <summary>The log category of the hunt's lines.</summary>
    public const string LogCategory = "hunt";

        /// <summary>The key of the Low B belt's profile (<c>profiles/low-b-belt.json</c>).</summary>
    public const string BeltKey = "low-b-belt";

    /// <summary>The key of the Low North Forest Mix's profile (<c>profiles/low-north-forest-mix.json</c>).</summary>
    public const string ForestKey = "low-north-forest-mix";

    /// <summary>
    /// Holtburg's centre on the map, the origin of <see cref="HuntCell.MapUnits"/> (42.5N 33.6E): a cell's distance is computed from the profile's map coordinates, so the rows keep the figures they were
    /// written with. Both hunt grounds are Holtburg's (their profiles' <c>home</c>).
    /// </summary>
    private const double HoltburgNorth = 42.5, HoltburgEast = 33.6;

    /// <summary>
    /// Every creature (ItemType 16) that the Low B belt's generators 1999 and 2001 spawn, three generator levels down, with its Level (int property 25): a read-only SELECT on the pinned release v0.9.296
    /// (the lead's, M4 1.2 fix slice, review 1.2-R1; 37 rows, levels 4 to 30), now the <c>outdoor.creatures</c> of the profile <see cref="BeltKey"/> (M5 item 2 g3). Keyed by <b>weenie class id</b> (the CreateObject carries it;
    /// names are not unique per level); the name is kept for the logs. The hunt's target is the nearest creature whose wcid is in this table and whose level is in the row's band: the Tumerok Warrior (233, level 30, through
    /// the Drudge Low Slave Camp) stays listed and the band filters it out, and a creature whose wcid is not listed is never a target.
    /// </summary>
    public static IReadOnlyList<HuntCreature> LowBTable { get; } = TableOf(BeltKey);

    /// <summary>The distinct names of <see cref="LowBTable"/>, in the table's order (the logs and the "names were not found" line).</summary>
    public static IReadOnlyList<string> LowBCreatures { get; } = [.. LowBTable.Select(c => c.Name).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The Low B belt, read from the profile <see cref="BeltKey"/>: generators 1999 and 2001, a ring 4.5 to 8 map units around Holtburg (42.5N 33.6E); ACE rolls children by cumulative probability, so the band is 8 to 20
    /// (the 1.1 SELECT's correction of spec 5.9). The ten cells are the south-west ones nearest the town, each stopped at in its centre; their terrain is the dat test's finding (<c>HuntGroundDatTests</c>,
    /// <c>HuntingGroundDatTests</c>).
    /// </summary>
    public static HuntGroundRow LowB { get; } = RowOf(BeltKey, LowBCreatures, LowBTable);

    /// <summary>
    /// Every creature (ItemType 16) that the Low North Forest Mix's generator 21183 spawns, six generator levels down, with its Level (int property 25): the lead's read-only SELECT on the pinned release
    /// v0.9.296 (M4 3.1, <c>cache/m4/3-1/forest-creatures.tsv</c>, 40 rows, levels 3 to 171), now the <c>outdoor.creatures</c> of the profile <see cref="ForestKey"/> (M5 item 2 g3). Keyed by <b>weenie class id</b> (the
    /// CreateObject carries it); the name is kept for the logs. In the table's order: level, then wcid. The two rows that are never a target by kind stay out: Fisana the Jeweler (411, an NPC, level 3) and the Tusker Shrine
    /// Statue (22641, level 171). The other 38 stay listed and the band (30 to 40) keeps the rest honest. The level is not on the wire (<c>TODO(m4-creature-level)</c>), so this table is its source; a creature whose wcid
    /// is not listed is never a target.
    /// </summary>
    public static IReadOnlyList<HuntCreature> LowNorthForestTable { get; } = TableOf(ForestKey);

    /// <summary>The distinct names of <see cref="LowNorthForestTable"/>, in the table's order (the logs and the "names were not found" line).</summary>
    public static IReadOnlyList<string> LowNorthForestCreatures { get; } = [.. LowNorthForestTable.Select(c => c.Name).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The Low North Forest Mix, read from the profile <see cref="ForestKey"/> (spec 5.9 item 2; generator 21183, the 0x9FB* landblocks 7.7 to 9.2 map units west of Holtburg): the forest ground, chosen with
    /// <c>--hunt-ground forest</c> (the belt stays the default). The band is <b>30 to 40</b>: the hunter is level 40, so the lead narrowed the spec's 30 to 60 (m4-plan.md 4.6; thirteen of the table's creatures are in it).
    /// The cells are <b>all 33</b> encounter spots of the strip (M5 item 2 g3, D42, <c>m5-hunt-band-empty</c>): a loop by landblock from the spot nearest Holtburg, each stopped at on the spot's own point (the generator's
    /// corner), the profile's cell cap (33) bounding the on-foot search. ACE rolls a spot's children by cumulative probability, so a cell may hold nothing and the hunt tries the next one, as on the belt. Their terrain is the
    /// dat test's finding (<c>HuntGroundDatTests</c>, 2026-10-21: all 33 read dry and flat, lowest normal z 0.973 or more; closes <c>m4-forest-cells (closed)</c>).
    /// </summary>
    public static HuntGroundRow LowNorthForestMix { get; } = RowOf(ForestKey, LowNorthForestCreatures, LowNorthForestTable);

    /// <summary>The rows, the used one first.</summary>
    public static IReadOnlyList<HuntGroundRow> Rows { get; } = [LowB, LowNorthForestMix];

    private static IReadOnlyList<HuntCreature> TableOf(string key) =>
        [.. OutdoorOf(key).Creatures.Select(c => new HuntCreature(c.Wcid, c.Name, c.Level) { SpawnMass = c.SpawnMass })];

    private static OutdoorGround OutdoorOf(string key) =>
        GroundCatalog.Get(key).Outdoor ?? throw new InvalidOperationException($"the hunting-ground profile '{key}' is not an outdoor ground");

    /// <summary>The row of the outdoor profile <paramref name="key"/>: its band, cells (each with its stop point), cell cap and the given table; in use (both are hunted today).</summary>
    private static HuntGroundRow RowOf(string key, IReadOnlyList<string> creatures, IReadOnlyList<HuntCreature> table)
    {
        var ground = GroundCatalog.Get(key);
        var outdoor = OutdoorOf(key);
        return new HuntGroundRow(
            ground.Name,
            ground.LevelBand.Min,
            ground.LevelBand.Max,
            creatures,
            [.. outdoor.Cells.Select(c => new HuntCell(c.Cell, c.North, c.East, Math.Round(Math.Sqrt(Math.Pow(c.North - HoltburgNorth, 2) + Math.Pow(c.East - HoltburgEast, 2)), 2), TerrainOf(c.Terrain)) { Spot = new Vector2(c.Spot.X, c.Spot.Y) })],
            InUse: true)
        {
            Table = table,
            CellCap = outdoor.CellCap,
        };
    }

    private static HuntTerrain TerrainOf(GroundTerrain terrain) => terrain switch
    {
        GroundTerrain.Dry => HuntTerrain.Dry,
        GroundTerrain.Water => HuntTerrain.Water,
        GroundTerrain.Steep => HuntTerrain.Steep,
        _ => HuntTerrain.Unread,
    };

    /// <summary>
    /// The height at the point the hunt goes to in the cell (<see cref="HuntCell.StopX"/>, <see cref="HuntCell.StopY"/>: the cell's centre, or the profile's spot), from the dats (<see cref="TerrainSampler.GetHeight"/>
    /// over the landblock's terrain), or null when the landblock has no terrain. The hunt stands the player 0.5 m above it (the server drops it to the floor).
    /// </summary>
    public static float? TerrainHeight(DatSet dats, HuntCell cell)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(cell);
        var terrain = dats.GetLandblockTerrain(LandblockCoord.FromId(cell.CellId));
        return terrain is null ? null : new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable).GetHeight(cell.StopX, cell.StopY);
    }

    /// <summary>The point a station's <c>place loc</c> sends the player to: the cell's centre at the terrain height plus 0.5 m (the journey's forest station places at the first cell's centre).</summary>
    public static Vector3 PlacePoint(HuntCell cell, float terrainHeight) => new(cell.X, cell.Y, terrainHeight + 0.5f);

    /// <summary>
    /// The point the hunt goes to in a cell (a <c>place loc</c> or a walk on foot): <see cref="HuntCell.StopX"/>, <see cref="HuntCell.StopY"/> (the profile's spot, else the centre) at the terrain height plus 0.5 m
    /// (M5 D42: the forest's walk stops at the encounter spot, not at the cell's centre).
    /// </summary>
    public static Vector3 StopPoint(HuntCell cell, float terrainHeight) => new(cell.StopX, cell.StopY, terrainHeight + 0.5f);

    /// <summary>The terrain type range of water (16 to 20, ACE's table, <c>LandblockStruct.cs:62</c>), as <c>HuntGroundDatTests</c> reads it.</summary>
    private const int FirstWaterType = 16, LastWaterType = 20;

    /// <summary>
    /// The height of a seek point (M4 P4 seek): the terrain height at (<paramref name="x"/>, <paramref name="y"/>) in the landblock of <paramref name="cellId"/>, or null when the landblock has no terrain, a corner of the
    /// 24 m cell holding the point is water, or a surface normal sampled over that cell (a 3 x 3 grid) is under <c>PhysicsGlobals.FloorZ</c> (the walker cannot stand there): the same rules the fixed cells were read with.
    /// </summary>
    public static float? SeekHeight(DatSet dats, uint cellId, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(dats);
        if (x < 0 || y < 0 || x >= 192 || y >= 192)
        {
            return null;
        }

        var terrain = dats.GetLandblockTerrain(LandblockCoord.FromId(cellId));
        if (terrain is null)
        {
            return null;
        }

        var cx = (int)(x / 24);
        var cy = (int)(y / 24);
        for (var vx = cx; vx <= cx + 1; vx++)
        {
            for (var vy = cy; vy <= cy + 1; vy++)
            {
                if (terrain.TerrainType(vx, vy) is >= FirstWaterType and <= LastWaterType)
                {
                    return null;
                }
            }
        }

        var sampler = new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable);
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                if (sampler.GetNormal((cx * 24) + 1 + (i * 11f), (cy * 24) + 1 + (j * 11f)).Z < Lifestone.Data.Physics.PhysicsGlobals.FloorZ)
                {
                    return null;
                }
            }
        }

        return sampler.GetHeight(x, y);
    }
}
