using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Client.Navigation;
using Lifestone.Client.World;

namespace Lifestone.Client.Tests.Navigation;

/// <summary>The HUD's position lines outdoors and inside EnvCells (audit finding position-hud-indoor).</summary>
public sealed class PositionReadoutTests
{
    [Fact]
    public void Outdoors_the_land_cell_and_retails_map_coordinates_are_shown()
    {
        var lines = PositionReadout.Describe(WorldLocations.Holtburg.Global, 0);
        Assert.Equal("Landblock 0xA9B4   cell 0xA9B40019   42.1N, 33.6E", lines[0]);
        Assert.Equal("Local  x 84.00  y 7.10  z 94.00", lines[1]);
    }

    [Fact]
    public void In_a_dungeon_the_cells_own_landblock_and_frame_are_shown_without_map_coordinates()
    {
        // The Town Network's drop point lies at y = -60 in landblock 0x0007: converted as an outdoor position it would
        // read "Landblock 0x0006 ... 96.6S, 101.7W" (the audit's evidence). Retail shows no coordinates in EnvCells.
        var town = InteriorLocations.TownNetwork.Location;
        var eye = town.Global + new Vector3(0f, 0f, 1.7f);
        var lines = PositionReadout.Describe(eye, town.Position.CellId, seenOutside: false);
        Assert.Equal("Landblock 0x0007   cell 0x00070143   indoors (dungeon)", lines[0]);
        Assert.Equal("Local  x 70.00  y -60.00  z 1.71", lines[1]);
        Assert.DoesNotContain(lines, l => l.Contains('S', StringComparison.Ordinal) && l.Contains('W', StringComparison.Ordinal));

        var mines = InteriorLocations.MinesOfDespair.Location;
        Assert.StartsWith("Landblock 0x0188   cell 0x01880307   indoors", PositionReadout.Describe(mines.Global, mines.Position.CellId)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_building_interior_says_so()
    {
        var shop = InteriorLocations.HoltburgShop.Location;
        var lines = PositionReadout.Describe(shop.Global, shop.Position.CellId, seenOutside: true);
        Assert.Equal("Landblock 0xA9B4   cell 0xA9B40163   indoors (building)", lines[0]);
        Assert.Equal("Local  x 100.00  y 36.00  z 94.00", lines[1]);
    }

    [Fact]
    public void Off_the_map_is_reported()
    {
        Assert.StartsWith("Off the map", PositionReadout.Describe(new GlobalPosition(-10, 5, 0), 0)[0], StringComparison.Ordinal);
    }
}
