using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Tests.Navigation;

/// <summary>Teleport destinations (ACE points of interest), map coordinate input and camera placement.</summary>
public sealed class NavigationTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly TeleportCatalog Catalog = TeleportCatalog.FromAce();

    public static readonly TheoryData<string> Towns = new()
    {
        "Holtburg", "Arwic", "Yaraq", "Shoushi", "Cragstone", "Rithwic", "Glenden Wood", "Al-Arqas", "Samsur", "Hebian-To",
        "Sawato", "Zaikhal", "Tufa", "Qalaba'r", "Lytelthorpe", "Eastham", "Baishi", "Mayoi", "Nanto", "Lin", "Dryreach",
        "Stonehold", "Plateau", "Fort Tethana",
    };

    [Theory]
    [MemberData(nameof(Towns))]
    public void Every_major_town_is_a_destination_with_retail_map_coordinates(string town)
    {
        var d = Catalog.Find(town);
        Assert.NotNull(d);
        Assert.Equal(TeleportCatalog.Normalize(town), TeleportCatalog.Normalize(d.Name));
        Assert.Equal(DestinationKind.Outdoor, d.Kind);
        Assert.NotNull(d.Map);
    }

    [Fact]
    public void The_catalogue_is_aces_56_portal_destinations_outdoor_first()
    {
        Assert.Equal(56, Catalog.Destinations.Count);
        var indoor = Catalog.Destinations.Where(d => d.Kind == DestinationKind.Indoor).Select(d => d.Name).ToArray();
        Assert.Equal(["AC Storage", "Hotel Swank!", "Night Club", "The Marketplace of Dereth", "Town Network", "Underground City"], indoor);
        Assert.True(Catalog.Destinations.TakeWhile(d => d.Kind == DestinationKind.Outdoor).Count() == 50);
        Assert.All(Catalog.Destinations, d => Assert.True(AcCoordinates.IsOnMap(d.Location.Position.Landblock), d.Name));
    }

    [Fact]
    public void Holtburg_is_the_same_drop_point_as_the_start_location()
    {
        var holtburg = Catalog.Find("holtburg")!;
        Assert.Equal(WorldLocations.Holtburg.Position, holtburg.Location.Position);
        Assert.Equal(WorldLocations.Holtburg.HeadingDegrees, holtburg.Location.HeadingDegrees, 6);
        Assert.Equal("42.1N, 33.6E", holtburg.Map.ToString());
        Assert.Contains("42820", holtburg.Source);
    }

    [Theory]
    [InlineData("hebian to", "Hebian-To")]
    [InlineData("QALABAR", "Qalaba'r")]
    [InlineData("swank", "Hotel Swank!")]
    [InlineData("TN", "Town Network")]
    [InlineData("glen", "Glenden Wood")]
    [InlineData("marketplace", "The Marketplace of Dereth")]
    public void Search_ignores_case_and_punctuation_and_knows_aces_aliases(string query, string expected)
    {
        Assert.Equal(expected, Catalog.Find(query)?.Name);
    }

    [Fact]
    public void Search_ranks_exact_then_prefix_then_substring()
    {
        var results = Catalog.Search("lin").Select(d => d.Name).ToArray();
        Assert.Equal("Lin", results[0]);
        Assert.Equal("Linvak Tukal", results[1]);
        Assert.Contains("Fiun Outpost", Catalog.Search("outpost").Select(d => d.Name));
        Assert.Empty(Catalog.Search("zzzz"));
        Assert.Equal(56, Catalog.Search("").Count);
        Assert.Null(Catalog.Find("  "));
    }

    [RequiresDatsFact]
    public void Outdoor_destinations_stand_on_the_ground()
    {
        // ACE's Destination origins are drop points on the landscape: their z is the retail ground height (within the
        // height of a step or a building floor). This pins the cell/origin interpretation of the extract.
        var dats = fixture.Dats;
        var offGround = new List<string>();
        foreach (var d in Catalog.Destinations.Where(d => d.Kind == DestinationKind.Outdoor))
        {
            var global = d.Location.Global;
            var block = AcCoordinates.LandblockAt(global)!.Value;
            var terrain = dats.GetLandblockTerrain(block)!;
            var sampler = new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable);
            var ground = sampler.GetHeight(d.Location.Position.Local.X, d.Location.Position.Local.Y);
            if (Math.Abs(ground - d.Location.Position.Local.Z) > 6.0)
            {
                offGround.Add($"{d.Name}: z {d.Location.Position.Local.Z} ground {ground}");
            }
        }

        Assert.True(offGround.Count <= 3, string.Join("; ", offGround));
    }

    [Theory]
    [InlineData("42.1N 33.6E", 0xA9B40019u)]
    [InlineData("42.1N, 33.6E", 0xA9B40019u)]
    [InlineData("33.6E 42.1N", 0xA9B40019u)]
    [InlineData("42.1 n 33.6 e", 0xA9B40019u)]
    [InlineData("42.1, 33.6", 0xA9B40019u)]
    [InlineData("42.14N 33.64E", 0xA9B40019u)] // rounded to retail's 0.1 grid
    [InlineData("33.3N 56.6E", 0xC6A90009u)] // Arwic
    [InlineData("0.0, 0.0", 0x7F7F001Cu)] // lcoord (1019, 1019)
    public void Map_coordinates_name_the_cell_retail_shows_them_for(string text, uint cell)
    {
        Assert.True(MapCoordinateParser.TryParse(text, out var map, out var error), error);
        Assert.True(MapCoordinateParser.TryGetCell(map, out var parsed));
        Assert.Equal($"0x{cell:X8}", $"0x{parsed:X8}");
    }

    [Fact]
    public void Map_coordinates_round_trip_through_retails_display_for_every_cell_on_a_grid()
    {
        for (var x = 0; x < AcCoordinates.LandCoordsPerSide; x += 7)
        {
            for (var y = 0; y < AcCoordinates.LandCoordsPerSide; y += 11)
            {
                var cell = AcCoordinates.LandCoordToCellId(x, y);
                Assert.True(AcCoordinates.TryGetMapCoordinates(cell, out var shown));
                Assert.True(MapCoordinateParser.TryParse(shown.ToString(), out var parsed, out var error), error);
                Assert.True(MapCoordinateParser.TryGetCell(parsed, out var back));
                Assert.Equal(cell, back);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("north")]
    [InlineData("42.1N")]
    [InlineData("42.1N 12.0S")]
    [InlineData("42.1N 33.6E 5")]
    [InlineData("150N 33E")]
    [InlineData("1 2 3")]
    public void Bad_map_coordinates_are_refused_with_a_message(string text)
    {
        Assert.False(MapCoordinateParser.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Map_coordinate_cell_centres_are_cell_centres()
    {
        Assert.True(MapCoordinateParser.TryParse("42.1N 33.6E", out var map, out _));
        Assert.True(MapCoordinateParser.TryGetCellCentre(map, out var centre));
        Assert.Equal(new CellPosition(0xA9B40019, new Vector3(84f, 12f, 0f)), centre);
    }

    [Fact]
    public void Overview_stands_back_and_looks_at_the_spot()
    {
        var target = new GlobalPosition(1000, 1000, 50);
        var pose = CameraPlacement.Overview(target, 90, _ => 0.0);
        Assert.Equal(new GlobalPosition(910, 1000, 95), pose.Eye);
        Assert.Equal(90.0, pose.HeadingDegrees, 9);
        Assert.Equal(-Math.Atan2(45, 90) * 180 / Math.PI, pose.PitchDegrees, 9);

        // Never below the ground plus the clearance.
        var hill = CameraPlacement.Overview(target, 90, _ => 200.0);
        Assert.Equal(205.0, hill.Eye.Z);
        Assert.Equal(-Math.Atan2(155, 90) * 180 / Math.PI, hill.PitchDegrees, 9);

        var straightDown = CameraPlacement.Overview(target, 33, _ => null, height: 100, distance: 0);
        Assert.Equal(-89.0, straightDown.PitchDegrees);
        Assert.Equal(33.0, straightDown.HeadingDegrees);
    }

    [Fact]
    public void Standing_puts_the_eye_above_the_ground()
    {
        var pose = CameraPlacement.Standing(new GlobalPosition(10, 20, 999), 45, _ => 30.0, 1.7);
        Assert.Equal(new GlobalPosition(10, 20, 31.7), pose.Eye);
        Assert.Equal((45.0, 0.0), (pose.HeadingDegrees, pose.PitchDegrees));
        Assert.Equal(1001.7, CameraPlacement.Standing(new GlobalPosition(0, 0, 1000), 0, _ => null, 1.7).Eye.Z);
    }

    [Fact]
    public void A_spot_without_height_takes_the_ground()
    {
        Assert.True(WorldLocations.TryParse("A9B4", out var block, out _));
        Assert.Equal(77.0, CameraPlacement.Spot(block, _ => 77.0).Z);
        Assert.Equal(94.0, CameraPlacement.Spot(WorldLocations.Holtburg, _ => 77.0).Z, 4);
    }
}
