using Lifestone.Client.Navigation;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Tests.Navigation;

/// <summary>
/// Fly mode's ground clearance (<see cref="FlyGroundClearance"/>): outdoors the eye stays 0.5 m above the ground, but
/// leaving a dungeon (or a cellar) through a wall or a floor no longer throws it up to the surface (review finding on the
/// Underground City, 0x01E901AD, whose cells lie about 10 m below its sea-level terrain).
/// </summary>
public sealed class FlyGroundClearanceTests
{
    private const uint DungeonCell = 0x01E901AD;
    private const uint BuildingCell = 0xA9B40163;
    private static readonly LandblockCoord Dungeon = LandblockCoord.FromId(DungeonCell);

    [Fact]
    public void Outdoors_the_eye_is_kept_half_a_metre_above_the_ground()
    {
        var clearance = new FlyGroundClearance();
        var outdoors = new FakeCell();
        Assert.Equal(30.5, clearance.Apply(At(5), At(-20), 30, 30, outdoors));
        Assert.Equal(40.0, clearance.Apply(At(41), At(40), 30, 30, outdoors));
        Assert.Equal(30.5, clearance.Apply(At(5), At(-20), 30, 30, null)); // without the interiors: always outdoors
        Assert.Equal(-20.0, clearance.Apply(At(5), At(-20), null, null, outdoors)); // no ground known: nothing to keep above
    }

    [Fact]
    public void Leaving_a_dungeon_cell_through_a_wall_keeps_the_eye_where_it_is()
    {
        var clearance = new FlyGroundClearance();
        var cell = new FakeCell { CellId = DungeonCell, SeenOutside = false, Near = true };

        // Out through the wall into the space between cells, 10 m under the sea: not lifted, then free around the cells.
        Assert.Equal(-10.3, clearance.Apply(At(-10.3), At(-10.3), 0, 0, cell));
        Assert.Equal(Dungeon, clearance.FreeAround);
        cell.CellId = 0;
        cell.SeenOutside = null;
        Assert.Equal(-10.3, clearance.Apply(At(-10.3), At(-10.3), 0, 0, cell));
        Assert.Equal(-11.0, clearance.Apply(At(-10.3), At(-11.0), 0, 0, cell)); // Q down to a lower level

        // Under the ground it is never pushed up, however far from the cells it flies.
        cell.Near = false;
        Assert.Equal(-40.0, clearance.Apply(At(-11.0), At(-40.0), 0, 0, cell));
        Assert.Equal(Dungeon, clearance.FreeAround);

        // Back in the open air far from the dungeon's cells it is outdoors again: the next dive stops above the ground.
        Assert.Equal(30.0, clearance.Apply(At(-40.0), At(30.0), 0, 0, cell));
        Assert.Null(clearance.FreeAround);
        Assert.Equal(0.5, clearance.Apply(At(30.0), At(-5.0), 0, 0, cell));
    }

    [Fact]
    public void A_dungeon_above_its_terrain_still_reaches_levels_below_it_through_the_void()
    {
        // As the Mines of Despair: the terrain lies at z 0 and the eye leaves a cell at z 1.7, above it.
        var clearance = new FlyGroundClearance();
        var cell = new FakeCell { CellId = 0x01880307, SeenOutside = false, Near = true };
        Assert.Equal(1.2, clearance.Apply(At(1.7), At(1.2), 0, 0, cell));
        cell.CellId = 0;
        cell.SeenOutside = null;
        Assert.Equal(3.0, clearance.Apply(At(1.2), At(3.0), 0, 0, cell)); // above the terrain, but near the cells: still free
        Assert.Equal(0.2, clearance.Apply(At(3.0), At(0.2), 0, 0, cell));
        Assert.Equal(-6.0, clearance.Apply(At(0.2), At(-6.0), 0, 0, cell));
    }

    [Fact]
    public void From_a_building_interior_the_eye_stops_on_the_floor_but_goes_down_the_cellar_stairs()
    {
        // As the Holtburg shop: the floor lies on the ground (z 94.00 both); the room reaches z 103.
        var clearance = new FlyGroundClearance();
        var cell = new FakeCell { CellId = BuildingCell, SeenOutside = true, CellTop = 103 };

        // Inside the cell below ground + 0.5: not clamped.
        cell.Stays = true;
        Assert.Equal(94.2, clearance.Apply(At(95.7), At(94.2), 94, 94, cell));

        // Through the floor: no cell holds the eye there, so it stays where it was instead of sinking into the ground.
        cell.Stays = false;
        Assert.Equal(94.2, clearance.Apply(At(94.2), At(93.9), 94, 94, cell));
        Assert.Null(clearance.FreeAround);

        // Out through a wall at floor height: it leaves level, and outside (no cell) it is lifted above the ground.
        Assert.Equal(94.2, clearance.Apply(At(94.2), At(94.2), 94, 94, cell));
        cell.CellId = 0;
        cell.SeenOutside = null;
        cell.CellTop = null;
        Assert.Equal(94.5, clearance.Apply(At(94.2), At(94.2), 94, 94, cell));

        // Down the stairs into the cellar: the step stays in the cells, so it is not clamped.
        cell.CellId = BuildingCell;
        cell.SeenOutside = true;
        cell.CellTop = 103;
        cell.Stays = true;
        Assert.Equal(92.0, clearance.Apply(At(94.5), At(92.0), 94, 94, cell));
    }

    [Fact]
    public void Leaving_a_cellar_through_its_wall_keeps_the_eye_under_the_ground_until_it_comes_up()
    {
        // As Holtburg's 0xA9B40180: seen_outside like every town cell, a cellar because its top (77.2) is under the ground (79.07).
        var clearance = new FlyGroundClearance();
        var cell = new FakeCell { CellId = BuildingCell + 1, SeenOutside = true, CellTop = 77.2, Near = true };
        Assert.Equal(73.5, clearance.Apply(At(73.5), At(73.5), 79.07, 79.07, cell));
        Assert.Equal(LandblockCoord.FromId(BuildingCell), clearance.FreeAround);
        cell.CellId = 0;
        cell.SeenOutside = null;
        cell.CellTop = null;
        Assert.Equal(73.5, clearance.Apply(At(73.5), At(73.5), 90.75, 90.75, cell));

        // Up into the open air: outdoors again at once, even next to the town's cells (unlike a dungeon).
        Assert.Equal(96.0, clearance.Apply(At(73.5), At(96.0), 90.75, 90.75, cell));
        Assert.Null(clearance.FreeAround);
        Assert.Equal(91.25, clearance.Apply(At(96.0), At(90.0), 90.75, 90.75, cell));
    }

    [Fact]
    public void An_explicit_outdoor_placement_under_the_ground_is_lifted_once()
    {
        // As --interior-eye on an outdoor cell under the terrain (visual audit finding free-camera-under-terrain).
        var clearance = new FlyGroundClearance();
        var cell = new FakeCell { CellId = DungeonCell, SeenOutside = false, Near = true };
        clearance.Apply(At(-10), At(-10), 0, 0, cell);
        Assert.NotNull(clearance.FreeAround);

        clearance.Placed();
        cell.CellId = 0;
        cell.SeenOutside = null;
        Assert.Equal(84.5, clearance.Apply(At(80), At(80), 84, 84, cell));
        Assert.Equal(84.5, clearance.Apply(At(84.5), At(84.5), 84, 84, cell));
    }

    private static GlobalPosition At(double z) => new(100.0, 100.0, z);

    private sealed class FakeCell : IViewerCell
    {
        public uint CellId { get; set; }

        public bool? SeenOutside { get; set; }

        public double? CellTop { get; set; }

        public bool Stays { get; set; }

        public bool Near { get; set; }

        public bool StaysIndoors(GlobalPosition eye) => AcCoordinates.IsEnvCell(CellId) && Stays;

        public bool NearCells(LandblockCoord landblock, GlobalPosition eye, float margin) => Near;
    }
}
