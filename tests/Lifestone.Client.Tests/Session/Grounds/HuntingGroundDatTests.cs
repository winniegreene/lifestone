using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Physics;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// The hunting-ground profiles against the retail dats (M5 item 2 g3; DoD 4, D42, D43, D46). One check per profile, whatever the ground: every dungeon's arrival is a place a human stands (inside its cell, over a floor);
/// every outdoor cell reads the terrain its profile says (water by the terrain type of the cell's four corner vertices, types 16 to 20, ACE's table <c>LandblockStruct.cs:62</c>; slope by the surface normal under
/// a 5 x 5 grid of points against <see cref="PhysicsGlobals.FloorZ"/>, as M4's <c>HuntGroundDatTests</c> read the same cells); every forest spot, the generator's corner the hunt now stops at, is dry and walkable
/// (<c>m5-hunt-band-empty</c>); and every dungeon's components cover the war line's formula at every spell level its spell plan names. Tests that need the dats skip when the dats are missing.
/// </summary>
public sealed class HuntingGroundDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const int FirstWater = 16, LastWater = 20, Grid = 5;

    /// <summary>How far under the arrival's z a floor may be and still be stood on (the origin is the player's feet; the dats' floors are polygons, the origin a figure of the world data), in metres.</summary>
    private const float FloorSlackMetres = 0.5f;

    public static TheoryData<string> OutdoorKeys() => new() { HuntGround.BeltKey, HuntGround.ForestKey };

    /// <summary>What the dats say of an outdoor cell: water at a corner vertex, else a normal under the physics floor, else dry (the rule of <c>HuntGroundDatTests.Read</c>).</summary>
    private GroundTerrain Read(OutdoorCell cell, out float lowestNormalZ)
    {
        var terrain = fixture.Dats.GetLandblockTerrain(LandblockCoord.FromId(cell.Cell)) ?? throw new InvalidOperationException("no terrain for " + LandblockCoord.FromId(cell.Cell));
        var index = (int)(cell.Cell & 0xFFFF) - 1;
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
                lowestNormalZ = Math.Min(lowestNormalZ, sampler.GetNormal((cx * 24) + 1 + (i * 22f / (Grid - 1)), (cy * 24) + 1 + (j * 22f / (Grid - 1))).Z);
            }
        }

        return water ? GroundTerrain.Water : lowestNormalZ < PhysicsGlobals.FloorZ ? GroundTerrain.Steep : GroundTerrain.Dry;
    }

    /// <summary>The keys of every dungeon profile (D46: a new dungeon's JSON file joins the dungeon checks here with no edit).</summary>
    public static TheoryData<string> DungeonKeys() => new(GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon).Select(g => g.Key));

    [RequiresDatsTheory]
    [MemberData(nameof(DungeonKeys))]
    public void A_dungeons_arrival_is_a_place_a_human_stands_inside_its_cell_over_a_floor_of_its_walkable_set(string key)
    {
        var ground = GroundCatalog.Get(key);
        var arrival = ground.Arrival;
        var coord = LandblockCoord.FromId(arrival.Cell);
        var cells = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), coord);

        var cell = cells.GetCell(arrival.Cell);
        Assert.NotNull(cell);
        Assert.True(cells.Contains(cell, arrival.Origin), $"the arrival point {arrival.Origin} is not inside cell 0x{arrival.Cell:X8} (CEnvCell::point_in_cell)");
        Assert.Equal(arrival.Cell, cells.FindCell(arrival.Origin, arrival.Cell)!.Id);

        // Over a floor: a floor polygon of the cell holds the point's footprint, and the feet are on it (within FloorSlackMetres above or below the plane).
        var local = cell.Frame.ParentToLocal(arrival.Origin);
        var floors = cells.GetStructure(cell).Floors.Where(f => FootprintHolds(f.Vertices, local.X, local.Y)).ToArray();
        Assert.NotEmpty(floors);
        var heights = floors.Select(f => local.Z - (-(f.Plane.D + (f.Plane.Normal.X * local.X) + (f.Plane.Normal.Y * local.Y)) / f.Plane.Normal.Z)).ToArray();
        output.WriteLine($"arrival 0x{arrival.Cell:X8} {arrival.Origin}: cell-local {local}, {floors.Length} floor polygon(s) under the footprint, feet above the plane by {string.Join(", ", heights.Select(h => h.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)))} m");
        Assert.Contains(heights, h => h >= -FloorSlackMetres && h <= FloorSlackMetres);

        Assert.True(ground.IsWalkable(arrival.Cell), $"{key}: the arrival cell is not a walkable cell of the profile");
        Assert.True(ground.InLandblocks(arrival.Cell));
        Assert.NotNull(new RoomGraph(cells, ground.Walk.DoorwayLean).RoomFrame(arrival.Cell)); // the room graph at the profile's lean has a room to walk from
    }

    [RequiresDatsTheory]
    [MemberData(nameof(OutdoorKeys))]
    public void Every_cell_of_the_profile_reads_the_terrain_it_says(string key)
    {
        var ground = GroundCatalog.Get(key);
        var disagree = new List<string>();
        foreach (var cell in ground.Outdoor!.Cells)
        {
            var read = Read(cell, out var normalZ);
            output.WriteLine($"{key} 0x{cell.Cell:X8} ({cell.North:0.00}N {cell.East:0.00}E): read {read}, lowest normal z {normalZ:0.000} (floor {PhysicsGlobals.FloorZ:0.000}); the profile says {cell.Terrain}");
            if (read != cell.Terrain)
            {
                disagree.Add($"0x{cell.Cell:X8} reads {read} (lowest normal z {normalZ:0.000}), the profile says {cell.Terrain}");
            }
        }

        Assert.True(disagree.Count == 0, $"{key}: cells the dats do not read as the profile says: " + string.Join("; ", disagree));
        Assert.All(ground.Outdoor.Cells, c => Assert.NotEqual(GroundTerrain.Unread, c.Terrain));
    }

    [RequiresDatsFact]
    public void Every_forest_spot_is_dry_and_walkable_and_the_stop_point_reads_a_terrain_height()
    {
        var forest = GroundCatalog.Get(HuntGround.ForestKey);
        Assert.Equal(33, forest.Outdoor!.Cells.Count);
        var failures = new List<string>();
        foreach (var cell in forest.Outdoor.Cells)
        {
            var coord = LandblockCoord.FromId(cell.Cell);
            var terrain = fixture.Dats.GetLandblockTerrain(coord) ?? throw new InvalidOperationException("no terrain for " + coord);
            var sampler = new TerrainSampler(terrain, fixture.Dats.TerrainRegion.LandHeightTable);
            var index = (int)(cell.Cell & 0xFFFF) - 1;

            // The spot lies in its own cell (it is the cell's low corner) and in the landblock, and its vertex and the cell's corners are not water ...
            var inCell = (int)(cell.Spot.X / 24) == index / 8 && (int)(cell.Spot.Y / 24) == index % 8 && cell.Spot.X >= 0 && cell.Spot.Y >= 0 && cell.Spot.X < 192 && cell.Spot.Y < 192;
            // ... the hunt's own seek rule accepts it (HuntGround.SeekHeight: no water at the cell's corners, every normal over its 3 x 3 grid walkable) and the normal at the spot itself is walkable ...
            var height = inCell ? HuntGround.SeekHeight(fixture.Dats, cell.Cell, cell.Spot.X, cell.Spot.Y) : null;
            var normalZ = inCell ? sampler.GetNormal(cell.Spot.X, cell.Spot.Y).Z : float.NaN;
            output.WriteLine($"0x{cell.Cell:X8} spot ({cell.Spot.X:0}, {cell.Spot.Y:0}): in cell {inCell}, seek height {height?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "none"}, normal z at the spot {normalZ:0.000}");
            if (!inCell || height is null || !(normalZ >= PhysicsGlobals.FloorZ))
            {
                failures.Add($"0x{cell.Cell:X8} spot ({cell.Spot.X:0}, {cell.Spot.Y:0}): in cell {inCell}, height {height?.ToString() ?? "none (water or too steep)"}, normal z {normalZ:0.000}");
            }

            // ... and the hunt's stop point reads a height through the row, the one the hunt places the player at.
            var row = HuntGround.LowNorthForestMix.Cells.Single(c => c.CellId == cell.Cell);
            Assert.Equal((cell.Spot.X, cell.Spot.Y), (row.StopX, row.StopY));
            Assert.NotNull(HuntGround.TerrainHeight(fixture.Dats, row));
        }

        Assert.True(failures.Count == 0, "forest spots that are not dry and walkable: " + string.Join("; ", failures));
    }

    [RequiresDatsTheory]
    [MemberData(nameof(DungeonKeys))]
    public void A_dungeons_components_cover_the_war_lines_formula_at_every_spell_level_its_plan_names(string key)
    {
        var ground = GroundCatalog.Get(key);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.NotEmpty(ground.SpellPlan);
        foreach (var row in ground.SpellPlan)
        {
            var line = ArenaProfile.Lines.Single(l => l.Name == row.Family);
            var spell = line.Spells.Single(s => s.Position == row.SpellLevel);
            var info = spells[spell.Id];
            Assert.Equal(spell.Name, info.Name);

            // The cast of a mage that holds its school's foci burns the foci formula (the kit carries it: GearKit.FociFormulaCounts): the level's scarab, then Prismatic Tapers by its power.
            var needed = GearKit.FociFormulaCounts(info.Formula);
            var have = ground.Components.Where(c => c.SpellLevel == row.SpellLevel).ToDictionary(c => c.Wcid, c => c.PerCast);
            var wanted = needed.ToDictionary(
                n => ComponentWeenies.TryGetByComponent(n.Key, out var w) ? w.Wcid : throw new InvalidOperationException($"component {n.Key} of {info.Name} is not in the component map"),
                n => n.Value);
            output.WriteLine($"{info.Name} (spell {spell.Id}, character levels {row.MinLevel}-{row.MaxLevel}): the formula needs {string.Join(", ", wanted.Select(w => $"{w.Value} x wcid {w.Key}"))}; the profile holds {string.Join(", ", have.Select(h => $"{h.Value} x wcid {h.Key}"))}");

            Assert.NotEmpty(wanted);
            foreach (var (wcid, count) in wanted)
            {
                Assert.True(have.TryGetValue(wcid, out var perCast) && perCast >= count, $"{info.Name}: the formula burns {count} x wcid {wcid}, the profile's components for spell level {row.SpellLevel} hold {(have.TryGetValue(wcid, out var p) ? p.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}");
            }

            Assert.Equal(wanted.Keys.Order(), have.Keys.Order()); // and nothing the formula does not burn
        }
    }

    /// <summary>True when (x, y) lies inside the polygon's footprint (even-odd rule over the cell-local vertices' x and y).</summary>
    private static bool FootprintHolds(IReadOnlyList<Vector3> vertices, float x, float y)
    {
        var inside = false;
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            var a = vertices[i];
            var b = vertices[j];
            if ((a.Y > y) != (b.Y > y) && x < ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
