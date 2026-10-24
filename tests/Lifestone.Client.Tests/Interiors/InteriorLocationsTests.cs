using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Client.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Interiors;

public sealed class InteriorLocationsTests(InteriorsFixture fixture) : IClassFixture<InteriorsFixture>
{
    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();
        foreach (var location in InteriorLocations.All)
        {
            data.Add(location.Key);
        }

        return data;
    }

    [Fact]
    public void There_are_at_least_three_dungeons_and_a_building_interior()
    {
        Assert.True(InteriorLocations.All.Count(l => l.Kind == InteriorKind.Dungeon) >= 3);
        Assert.Contains(InteriorLocations.All, l => l.Kind == InteriorKind.BuildingInterior);
        Assert.Equal(InteriorLocations.All.Count, InteriorLocations.All.Select(l => l.Key).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Keys_parse_as_start_locations(string key)
    {
        Assert.True(InteriorLocations.TryGet(key.ToUpperInvariant().Replace('-', '_'), out var location));
        Assert.True(WorldLocations.TryParse(key, out var parsed, out var error), error);
        Assert.Equal(location, parsed);
        Assert.True(AcCoordinates.IsEnvCell(parsed.Position.CellId));
        Assert.True(parsed.HasHeight);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsTheory]
    [MemberData(nameof(Keys))]
    public void Every_location_stands_on_a_floor_inside_its_cell(string key)
    {
        var entry = InteriorLocations.All.Single(l => l.Key == key);
        var block = fixture.Block(entry.Location.Position.Landblock);
        var cell = block.Cells.GetCell(entry.CellId);
        Assert.NotNull(cell);
        var feet = entry.Location.Position.Local;

        // The feet just above the floor and the eyes (1.7 m up) are both in the cell (retail CEnvCell::point_in_cell).
        Assert.True(block.Cells.Contains(cell, feet + new Vector3(0f, 0f, 0.05f)), $"{key}: feet not in 0x{cell.Id:X8}");
        Assert.True(block.Cells.Contains(cell, feet + new Vector3(0f, 0f, 1.7f)), $"{key}: eyes not in 0x{cell.Id:X8}");
        Assert.True(CellQueries.IsEnclosed(block.Cells, cell, feet + new Vector3(0f, 0f, 1.7f)), $"{key}: not under a ceiling");

        // A walkable floor polygon lies right under the point.
        var floor = CellQueries.FloorBelow(block.Cells, cell, feet + new Vector3(0f, 0f, 1.7f), feet.Z + 0.5f);
        Assert.NotNull(floor);
        Assert.InRange(floor.Value, feet.Z - 0.05f, feet.Z + 0.05f);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsTheory]
    [MemberData(nameof(Keys))]
    public void Dungeons_are_dungeon_landblocks_and_interiors_are_building_cells(string key)
    {
        var entry = InteriorLocations.All.Single(l => l.Key == key);
        var coord = entry.Location.Position.Landblock;
        var block = fixture.Block(coord);
        var cell = block.Cells.GetCell(entry.CellId)!;
        var statics = LandblockStatics.Read(fixture.Dats, coord);
        if (entry.Kind == InteriorKind.Dungeon)
        {
            Assert.False(cell.SeenOutside);
            var summary = CellDiscovery.Summarize(block.Cells, statics, fixture.Dats.GetLandblockTerrain(coord), fixture.Dats.TerrainRegion.LandHeightTable);
            Assert.Equal(LandblockCellKind.Dungeon, summary.Kind);
        }
        else
        {
            Assert.True(cell.SeenOutside);
            Assert.Contains(entry.CellId, CellDiscovery.BuildingCellIds(block.Cells, statics));
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Dungeon_landblocks_have_an_entry_inside_and_towns_have_none()
    {
        foreach (var coord in new[] { new Lifestone.Data.Terrain.LandblockCoord(0x00, 0x07), new Lifestone.Data.Terrain.LandblockCoord(0x01, 0x63) })
        {
            var entry = DungeonEntries.Find(fixture.Structures, coord);
            Assert.NotNull(entry);
            var block = fixture.Block(coord);
            var cell = block.Cells.GetCell(entry.Position.CellId)!;
            Assert.False(cell.SeenOutside);
            Assert.True(block.Cells.Contains(cell, entry.Position.Local + new Vector3(0f, 0f, 0.05f)));
        }

        Assert.Null(DungeonEntries.Find(fixture.Structures, InteriorsFixture.Holtburg)); // a town (Mixed): stays outdoors
        Assert.Null(DungeonEntries.Find(fixture.Structures, new Lifestone.Data.Terrain.LandblockCoord(0x5A, 0x5A))); // open sea
    }

    [Fact]
    public void The_training_academy_is_where_ace_spawns_a_new_character()
    {
        // m2-plan §1.6 item 26 (ST12): CharGen StarterAreas[0].Locations[0], not M1's 0x860301AD.
        var academy = InteriorLocations.TrainingAcademy;
        Assert.Equal(0x860201ADu, academy.CellId);
        Assert.Equal(new Vector3(12.3199f, -28.482f, 0.005f), academy.Location.Position.Local);
        Assert.True(InteriorLocations.TryGet("training-academy", out var parsed));
        Assert.Equal(0x860201ADu, parsed.Position.CellId);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_training_academy_matches_the_portal_dats_first_holtburg_start_location()
    {
        Assert.True(fixture.Dats.TryGetPortal<DatReaderWriter.DBObjs.CharGen>(0x0E000002, out var chargen));
        var start = chargen.StartingAreas[0].Locations[0];
        var academy = InteriorLocations.TrainingAcademy;
        Assert.Equal(start.CellId, academy.CellId);
        Assert.True(Vector3.Distance(start.Frame.Origin, academy.Location.Position.Local) < 1e-3f, $"dat origin {start.Frame.Origin}");
        var datHeading = WorldLocations.HeadingFromQuaternion(start.Frame.Orientation.W, start.Frame.Orientation.Z);
        Assert.InRange(Math.Abs(((datHeading - academy.Location.HeadingDegrees + 540.0) % 360.0) - 180.0), 0.0, 0.01);

        // M1's cell is one of the same start area's other locations: the same room in another landblock.
        Assert.Contains(chargen.StartingAreas[0].Locations, l => l.CellId == 0x860301ADu);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Acviewers_hotel_swank_point_is_not_in_its_cell_which_is_why_it_is_not_curated()
    {
        var block = fixture.Block(new Lifestone.Data.Terrain.LandblockCoord(0x01, 0x8A));
        var cell = block.Cells.GetCell(0x018A024F)!;
        Assert.False(block.Cells.Contains(cell, new Vector3(120f, -79.9f, 0.05f)));
    }
}
