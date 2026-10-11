using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Client.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>Walk mode inside cells follows the floors from cell to cell, up and down ramps.</summary>
public sealed class CellWalkerTests(InteriorsFixture fixture) : IClassFixture<InteriorsFixture>
{
    [RequiresDatsTheory]
    [InlineData(0x0163010Eu, 0x0163012Au)] // Holtburg Redoubt: 3 m up a ramp
    [InlineData(0x0163012Au, 0x0163010Eu)] // and back down
    [InlineData(0x0188010Au, 0x0188010Bu)] // Mines of Despair: 1.5 m up
    public void Walking_from_floor_to_floor_follows_the_ramp_into_the_next_cell(uint fromCell, uint toCell)
    {
        var coord = Lifestone.Data.Terrain.LandblockCoord.FromId(fromCell);
        var block = fixture.Block(coord);
        var registry = fixture.Registry(coord);
        var tracker = new CellTracker(registry);
        var walker = new CellWalker(registry);
        var a = block.Cells.GetCell(fromCell)!;
        var b = block.Cells.GetCell(toCell)!;
        var start = CellDiscovery.FloorPointInCell(block.Cells, a)!.Position;
        var end = CellDiscovery.FloorPointInCell(block.Cells, b)!.Position;
        var eye = InteriorsFixture.Global(coord, start + new Vector3(0f, 0f, 1.7f));
        tracker.Place(a, eye);

        var steps = (int)(Vector2.Distance(new Vector2(start.X, start.Y), new Vector2(end.X, end.Y)) / 0.05f);
        for (var i = 1; i <= steps; i++)
        {
            var xy = Vector2.Lerp(new Vector2(start.X, start.Y), new Vector2(end.X, end.Y), i / (float)steps);
            var moved = InteriorsFixture.Global(coord, new Vector3(xy.X, xy.Y, 0f)) with { Z = eye.Z };
            tracker.Update(moved);
            Assert.True(tracker.IsIndoors);
            var next = walker.FollowFloor(moved, tracker.CellId, 1.7f);
            Assert.True(Math.Abs(next.Z - eye.Z) <= CellWalker.StepUpHeight, $"step {i}: {eye.Z} -> {next.Z}");
            eye = next;
            tracker.Update(eye);
        }

        Assert.Equal(toCell, tracker.CellId);
        Assert.Equal(end.Z + 1.7, eye.Z - AcCoordinates.LandblockOrigin(coord).Z, 2);
    }

    [RequiresDatsFact]
    public void The_floor_under_the_feet_is_found_and_without_a_floor_the_height_is_kept()
    {
        var location = InteriorLocations.TownNetwork;
        var coord = location.Location.Position.Landblock;
        var walker = new CellWalker(fixture.Registry(coord));
        var feet = location.Location.Global; // 5 mm above the floor
        var onFloor = walker.FollowFloor(feet with { Z = feet.Z + 1.7 }, location.CellId, 1.7f);
        Assert.Equal(feet.Z + 1.7, onFloor.Z, 1);

        // Far outside the cell's floors: unchanged.
        var away = feet with { X = feet.X + 500.0, Z = feet.Z + 50.0 };
        Assert.Equal(away, walker.FollowFloor(away, location.CellId, 1.7f));

        // Unknown landblock: unchanged.
        Assert.Equal(away, new CellWalker(new InteriorRegistry()).FollowFloor(away, location.CellId, 1.7f));
    }
}
