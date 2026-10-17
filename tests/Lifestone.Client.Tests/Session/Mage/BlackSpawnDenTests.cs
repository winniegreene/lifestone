using Lifestone.Client.Interiors;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>The den's arrival floor and its Places row (M3 3.1 s0; the den map is cache/m3/3-1/den-map.md).</summary>
public sealed class BlackSpawnDenTests
{
    private static readonly uint[] SpawnCells = [0x02E1, 0x02E2, 0x0303, 0x030D, 0x0310, 0x0320, 0x0325, 0x0338, 0x033F, 0x036F, 0x0377, 0x0383];

    [Fact]
    public void The_arrival_and_the_twelve_spawn_cells_are_the_floor()
    {
        Assert.True(BlackSpawnDen.IsFloorCell(0x01050375));
        foreach (var low in SpawnCells)
        {
            Assert.True(BlackSpawnDen.IsFloorCell(0x01050000 | low), $"0x{low:X4}");
        }

        Assert.Equal(158, BlackSpawnDen.FloorLowWords.Count); // the 13 above, then the corridor cells (lane fa; BlackSpawnDenFloorDatTests pins the rest)
    }

    [Fact]
    public void The_excluded_cells_are_never_the_floor()
    {
        foreach (var low in new uint[] { 0x0100, 0x0113, 0x0121, 0x016A, 0x0180, 0x018B, 0x0244, 0x02D3, 0x02D5, 0x02D6 })
        {
            Assert.True(BlackSpawnDen.IsExcluded(low), $"0x{low:X4}");
            Assert.False(BlackSpawnDen.IsFloorCell(0x01050000 | low), $"0x{low:X4}");
        }

        Assert.False(BlackSpawnDen.IsExcluded(0x0122));
        Assert.False(BlackSpawnDen.IsExcluded(0x0169));
        Assert.False(BlackSpawnDen.IsExcluded(0x018C));
    }

    [Fact]
    public void Another_landblock_or_an_unlisted_cell_is_not_the_floor()
    {
        Assert.False(BlackSpawnDen.IsFloorCell(0x01040375));
        Assert.False(BlackSpawnDen.IsFloorCell(0x01060375));
        Assert.False(BlackSpawnDen.IsFloorCell(0x01050154));
        Assert.False(BlackSpawnDen.IsFloorCell(0x0375));
        Assert.False(BlackSpawnDen.IsFloorCell(0));
    }

    [Fact]
    public void LeftFloor_is_true_only_for_a_den_cell_that_is_not_the_floor()
    {
        // 3.1-R4: the check both fight walks make every frame.
        Assert.False(BlackSpawnDen.LeftFloor(0x01050375)); // the arrival
        Assert.False(BlackSpawnDen.LeftFloor(0x01050303)); // a spawn cell
        Assert.True(BlackSpawnDen.LeftFloor(0x0105035C)); // the exit portal's doorway
        Assert.True(BlackSpawnDen.LeftFloor(0x01050341)); // the exit portal's room
        Assert.True(BlackSpawnDen.LeftFloor(0x010502D6)); // an outlier cell
        Assert.True(BlackSpawnDen.LeftFloor(0x01050110)); // the north wing
        Assert.True(BlackSpawnDen.LeftFloor(0x0105FFFF)); // beyond the mask
        Assert.False(BlackSpawnDen.LeftFloor(0x01040375)); // another landblock: not "left", somewhere else
        Assert.False(BlackSpawnDen.LeftFloor(0));
        Assert.False(BlackSpawnDen.LeftFloor(0xA9B40019));
    }

    [Fact]
    public void Only_tuskers_on_the_floor_are_targets()
    {
        Assert.True(BlackSpawnDen.IsTarget(1628, 0x01050303));
        Assert.True(BlackSpawnDen.IsTarget(1629, 0x01050375));

        // The north wing's and the outliers' creatures never pass, wherever they stand.
        foreach (var wcid in new uint[] { 23, 237, 1609, 1610 })
        {
            foreach (var low in SpawnCells)
            {
                Assert.False(BlackSpawnDen.IsTarget(wcid, 0x01050000 | low), $"wcid {wcid} 0x{low:X4}");
            }

            Assert.False(BlackSpawnDen.IsTarget(wcid, 0x01050154));
            Assert.False(BlackSpawnDen.IsTarget(wcid, 0x010502D6));
            Assert.False(BlackSpawnDen.IsTarget(wcid, 0x01050244));
        }

        // A tusker in the chest corner or the north wing is not a target.
        Assert.False(BlackSpawnDen.IsTarget(1628, 0x010502D6));
        Assert.False(BlackSpawnDen.IsTarget(1629, 0x010502D6));
        Assert.False(BlackSpawnDen.IsTarget(1629, 0x0105010D));
    }

    [Fact]
    public void The_arrival_is_the_portals_destination()
    {
        Assert.Equal(ArenaProfile.ArenaCell, BlackSpawnDen.Arrival.CellId);
        Assert.Equal(0x01050375u, BlackSpawnDen.Arrival.CellId);
        Assert.Equal(100f, BlackSpawnDen.Arrival.Origin.X);
        Assert.Equal(-210f, BlackSpawnDen.Arrival.Origin.Y);
        Assert.Equal(0f, BlackSpawnDen.Arrival.Origin.Z);
        Assert.Equal(0.707107f, BlackSpawnDen.Arrival.Orientation.W, 5);
        Assert.Equal(-0.707107f, BlackSpawnDen.Arrival.Orientation.Z, 5);
    }

    [Fact]
    public void The_places_row_reaches_the_catalog_at_the_arrival()
    {
        var row = InteriorLocations.BlackSpawnDenAluvian;
        Assert.Contains(row, InteriorLocations.All);
        Assert.Equal("Black Spawn Den (Aluvian)", row.Name);
        Assert.Equal(InteriorKind.Dungeon, row.Kind);
        Assert.Equal(BlackSpawnDen.Arrival.CellId, row.CellId);
        Assert.True(InteriorLocations.TryGet("black spawn den aluvian", out var location));
        Assert.Equal(row.Location, location);

        var catalog = TeleportCatalog.Default();
        Assert.Contains(catalog.Destinations, d => d.Name == "Black Spawn Den (Aluvian)" && d.Location.Position.CellId == 0x01050375);
        Assert.Contains(catalog.Search("black spawn"), d => d.Name == "Black Spawn Den (Aluvian)");
    }
}
