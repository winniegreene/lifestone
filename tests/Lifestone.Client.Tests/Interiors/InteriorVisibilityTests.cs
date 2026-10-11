using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Data.Geometry;
using Lifestone.Data.Tests;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>Which cells are drawn and whether the landscape is (retail <c>PView::DrawInside</c> / <c>PView::DrawPortal</c>).</summary>
public sealed class InteriorVisibilityTests(InteriorsFixture fixture) : IClassFixture<InteriorsFixture>
{
    private static readonly LandblockCoord Holtburg = InteriorsFixture.Holtburg;

    public static TheoryData<string> Dungeons()
    {
        var data = new TheoryData<string>();
        foreach (var location in InteriorLocations.All.Where(l => l.Kind == InteriorKind.Dungeon))
        {
            data.Add(location.Key);
        }

        return data;
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Dungeons))]
    public void In_a_dungeon_only_the_viewer_cell_and_its_stab_list_are_drawn_and_never_the_landscape(string key)
    {
        var location = InteriorLocations.All.Single(l => l.Key == key);
        var coord = location.Location.Position.Landblock;
        var registry = fixture.Registry(coord);
        var tracker = new CellTracker(registry);
        var cell = fixture.Block(coord).Cells.GetCell(location.CellId)!;
        var eye = location.Location.Global with { Z = location.Location.Global.Z + 1.7 };
        tracker.Place(cell, eye);
        var pvs = PortalVisibility.PotentiallyVisibleSet(cell).ToHashSet();
        var union = new HashSet<uint>();
        for (var heading = 0; heading < 360; heading += 15)
        {
            var view = InteriorVisibility.Compute(registry, tracker, eye, InteriorsFixture.Looking(heading));
            Assert.False(view.LandscapeVisible);
            Assert.True(view.IndoorAmbient);
            Assert.Contains(cell.Id, view.VisibleCells);
            Assert.Subset(pvs, view.VisibleCells.ToHashSet());
            Assert.Equal(view.VisibleCells.Order(), view.ViewerCells.Order());
            Assert.Empty(view.Doorways);
            union.UnionWith(view.VisibleCells);
        }

        // The frustum matters: looking at the floor shows fewer cells than all directions together.
        var down = InteriorVisibility.Compute(registry, tracker, eye, InteriorsFixture.Looking(0, -89));
        Assert.True(union.Count == 1 || down.VisibleCells.Count < union.Count, $"{down.VisibleCells.Count} of {union.Count}");
    }

    [RequiresDatsFact]
    public void In_a_building_the_landscape_is_drawn_only_when_an_outside_portal_is_in_view()
    {
        var registry = fixture.Registry(Holtburg);
        var tracker = new CellTracker(registry);
        var location = InteriorLocations.HoltburgSmithy;
        var cell = fixture.Block(Holtburg).Cells.GetCell(location.CellId)!;
        var eye = location.Location.Global with { Z = location.Location.Global.Z + 1.7 };
        tracker.Place(cell, eye);

        // North: a window. East: the back wall and an inner archway (screenshots interiors-holtburg-b0-h0/h90).
        var north = InteriorVisibility.Compute(registry, tracker, eye, InteriorsFixture.Looking(0, -10));
        var east = InteriorVisibility.Compute(registry, tracker, eye, InteriorsFixture.Looking(90, -10));
        Assert.True(north.LandscapeVisible);
        Assert.False(east.LandscapeVisible);
        Assert.False(north.IndoorAmbient); // seen_outside: the sky's ambient
        Assert.False(east.IndoorAmbient);
        Assert.All(new[] { north, east }, v => Assert.Contains(cell.Id, v.ViewerCells));
    }

    [RequiresDatsFact]
    public void Outdoors_a_doorway_in_view_shows_its_cells_clipped_to_its_cone()
    {
        var registry = fixture.Registry(Holtburg);
        var tracker = new CellTracker(registry);
        var eye = InteriorsFixture.Global(Holtburg, new Vector3(90.5f, 36f, 95.7f)); // 7 m west of the shop's west door
        tracker.Place(null, eye);
        var statics = LandblockStatics.Read(fixture.Dats, Holtburg)!;
        var westDoor = statics.Buildings[7].Portals[1];

        var view = InteriorVisibility.Compute(registry, tracker, eye, InteriorsFixture.Looking(90));
        Assert.True(view.LandscapeVisible);
        Assert.Empty(view.ViewerCells);
        Assert.Contains(westDoor.OtherCellId, view.VisibleCells);
        Assert.NotEmpty(view.Doorways);
        Assert.All(view.VisibleCells, id =>
        {
            Assert.True(view.CellDoorways.TryGetValue(id, out var rows));
            Assert.InRange(rows.Length, 1, InteriorVisibility.MaxDoorwaysPerCell);
            Assert.All(rows, r => Assert.InRange(r, 0, view.Doorways.Count - 1));
        });

        // Only cells of the doorway's stab lists can be seen through it (PView::DrawPortal → add_views).
        var stabs = statics.Buildings.SelectMany(b => b.Portals).SelectMany(p => p.StabList).ToHashSet();
        Assert.Subset(stabs, view.VisibleCells.ToHashSet());

        // Facing away, the shop is not looked into.
        var away = InteriorVisibility.Compute(registry, tracker, eye, InteriorsFixture.Looking(270));
        Assert.DoesNotContain(westDoor.OtherCellId, away.VisibleCells);
    }

    [Fact]
    public void Cone_and_box_test_is_conservative()
    {
        // A cone looking along +Y from the origin, 45° to each side.
        var cone = new[]
        {
            new CellPlane(Vector3.Normalize(new Vector3(1f, 1f, 0f)), 0f),
            new CellPlane(Vector3.Normalize(new Vector3(-1f, 1f, 0f)), 0f),
        };
        Assert.True(InteriorVisibility.ConeTouchesBox(cone, new BoundingBox(new Vector3(-1f, 5f, -1f), new Vector3(1f, 6f, 1f))));
        Assert.True(InteriorVisibility.ConeTouchesBox(cone, new BoundingBox(new Vector3(4f, 4.5f, 0f), new Vector3(6f, 6f, 1f)))); // straddles
        Assert.False(InteriorVisibility.ConeTouchesBox(cone, new BoundingBox(new Vector3(-1f, -6f, -1f), new Vector3(1f, -5f, 1f)))); // behind
        Assert.False(InteriorVisibility.ConeTouchesBox(cone, new BoundingBox(new Vector3(8f, 1f, 0f), new Vector3(9f, 2f, 1f)))); // aside
    }
}
