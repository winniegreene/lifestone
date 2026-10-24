using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Data.Tests;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>The viewer's cell as the camera moves (retail <c>CObjCell::find_cell_list</c> rules plus the free-camera search).</summary>
[Trait("Tier", "Exhaustive")]
public sealed class CellTrackerTests(InteriorsFixture fixture) : IClassFixture<InteriorsFixture>
{
    private static readonly LandblockCoord Holtburg = InteriorsFixture.Holtburg;

    /// <summary>Holtburg building 7 (the shop): its west doorway is at x = 97.5, y = 36, z 94-96.5.</summary>
    private static readonly int ShopBuilding = 7;

    [RequiresDatsFact]
    public void Walking_in_through_a_doorway_enters_the_doorway_cell_and_walking_out_leaves_through_the_outside_portal()
    {
        var registry = fixture.Registry(Holtburg);
        var tracker = new CellTracker(registry);
        var outside = InteriorsFixture.Global(Holtburg, new Vector3(94f, 36f, 95.7f));
        tracker.Place(null, outside);
        Assert.False(tracker.Update(outside));
        Assert.False(tracker.IsIndoors);

        var shopCells = BuildingCells(ShopBuilding);
        var inside = InteriorsFixture.Global(Holtburg, new Vector3(99f, 36f, 95.7f));
        Assert.True(tracker.Update(inside));
        Assert.Equal(CellTransition.Doorway, tracker.LastTransition);
        Assert.Contains(tracker.CellId, shopCells);
        Assert.True(tracker.Cell!.SeenOutside);
        Assert.False(tracker.IsInDungeonCell);

        // Deeper in: to the counter room through inner portals (or still the same cell).
        var deeper = InteriorsFixture.Global(Holtburg, new Vector3(104f, 36f, 95.7f));
        tracker.Update(deeper);
        Assert.True(tracker.IsIndoors);
        Assert.Contains(tracker.CellId, shopCells);
        Assert.True(tracker.LastTransition is CellTransition.None or CellTransition.Portal);

        // Back out the way we came.
        tracker.Update(inside);
        Assert.True(tracker.Update(outside));
        Assert.False(tracker.IsIndoors);
        Assert.Equal(CellTransition.Outside, tracker.LastTransition);
    }

    [RequiresDatsFact]
    public void A_free_camera_flying_through_a_wall_is_found_by_the_search_and_through_a_roof_opening_by_the_doorway()
    {
        var registry = fixture.Registry(Holtburg);
        var tracker = new CellTracker(registry);
        var smithyCells = BuildingCells(0);

        // Through the smithy's west wall (its west door is at y 131.5; this is 4.5 m south of it): only the search finds it.
        var outside = InteriorsFixture.Global(Holtburg, new Vector3(70f, 127f, 67.7f));
        tracker.Place(null, outside);
        tracker.Update(outside);
        Assert.False(tracker.IsIndoors);
        tracker.Update(InteriorsFixture.Global(Holtburg, new Vector3(80f, 127f, 67.7f)));
        Assert.True(tracker.IsIndoors);
        Assert.Equal(CellTransition.Search, tracker.LastTransition);
        Assert.Contains(tracker.CellId, smithyCells);

        // The smithy's first three building portals are horizontal openings in its roof (z = 71): dropping through one
        // is a doorway crossing, as for a player.
        var smithy = InteriorLocations.HoltburgSmithy.Location.Position.Local + new Vector3(0f, 0f, 1.7f);
        var above = InteriorsFixture.Global(Holtburg, smithy + new Vector3(0f, 0f, 30f));
        tracker.Place(null, above);
        tracker.Update(above);
        Assert.False(tracker.IsIndoors);
        tracker.Update(InteriorsFixture.Global(Holtburg, smithy));
        Assert.Equal(CellTransition.Doorway, tracker.LastTransition);
        Assert.Contains(tracker.CellId, smithyCells);
    }

    [RequiresDatsFact]
    public void Outdoor_points_around_the_town_are_only_taken_for_rooms_inside_a_building()
    {
        // Eye height over the terrain on a 1 m grid over Holtburg. Retail's point_in_cell alone (a convex BSP region)
        // claims points outside the buildings; the search, which also wants a floor and a ceiling around the eye, may only
        // claim points within a building's model bounds, or in one of the landblock's cells that belong to no building
        // (0xA9B40183 is a small stone room set into the ground at the terrain's level, entered by its own outside portal).
        var block = fixture.Block(Holtburg);
        var buildingCells = CellDiscovery.BuildingCellIds(block.Cells, LandblockStatics.Read(fixture.Dats, Holtburg));
        var registry = fixture.Registry(Holtburg);
        var tracker = new CellTracker(registry);
        var statics = LandblockStatics.Read(fixture.Dats, Holtburg)!;
        var meshes = new Lifestone.Data.Models.GfxObjMeshCache(fixture.Dats);
        var shells = statics.Buildings.Select(b =>
        {
            var bounds = meshes.Get(b.ModelId)!.VertexBounds;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? bounds.Min.X : bounds.Max.X, (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y, (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
                var p = b.Frame.LocalToParent(corner);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            return (Min: min, Max: max);
        }).ToList();

        var bspOnly = 0;
        var found = 0;
        for (var x = 0.5f; x < 192f; x += 1f)
        {
            for (var y = 0.5f; y < 192f; y += 1f)
            {
                var eye = new Vector3(x, y, fixture.TerrainHeight(Holtburg, x, y) + 1.7f);
                var inShell = shells.Any(s => eye.X >= s.Min.X && eye.Y >= s.Min.Y && eye.Z >= s.Min.Z && eye.X <= s.Max.X && eye.Y <= s.Max.Y && eye.Z <= s.Max.Z);
                if (!inShell && block.Cells.Cells.Any(c => block.Contains(c, eye)))
                {
                    bspOnly++;
                }

                if (tracker.Search(InteriorsFixture.Global(Holtburg, eye)) is { } cell)
                {
                    found++;
                    Assert.True(inShell || !buildingCells.Contains(cell.Id), $"({x}, {y}) taken for room 0x{cell.Id:X8} outside every building");
                }
            }
        }

        Assert.True(found > 100); // the grid does pass through rooms
        Assert.True(bspOnly > 0); // and the BSP test alone would have been fooled
    }

    [RequiresDatsFact]
    public void Inside_a_dungeon_the_cell_follows_the_portals()
    {
        var location = InteriorLocations.TownNetwork;
        var coord = location.Location.Position.Landblock;
        var registry = fixture.Registry(coord);
        var tracker = new CellTracker(registry);
        var block = fixture.Block(coord);
        var start = block.Cells.GetCell(location.CellId)!;
        var eye = location.Location.Position.Local + new Vector3(0f, 0f, 1.7f);
        tracker.Place(start, InteriorsFixture.Global(coord, eye));
        Assert.True(tracker.IsInDungeonCell);
        tracker.Update(InteriorsFixture.Global(coord, eye));
        Assert.Equal(start.Id, tracker.CellId);

        // Walk to the middle of every neighbour behind an inside portal that contains its own centre at eye height.
        var visited = 0;
        foreach (var portal in start.Portals.Where(p => !p.LeadsOutside))
        {
            var other = block.Cells.GetCell(portal.OtherCellId)!;
            block.TryGetCellBounds(other.Id, out var bounds);
            var target = new Vector3(bounds.Center.X, bounds.Center.Y, eye.Z);
            if (!block.Cells.Contains(other, target) || block.Cells.Contains(start, target))
            {
                continue;
            }

            tracker.Place(start, InteriorsFixture.Global(coord, eye));
            tracker.Update(InteriorsFixture.Global(coord, target));
            Assert.Equal(other.Id, tracker.CellId);
            Assert.Equal(CellTransition.Portal, tracker.LastTransition);
            visited++;
        }

        Assert.True(visited >= 2);
    }

    [RequiresDatsFact]
    public void A_placed_cell_whose_landblock_is_not_loaded_is_kept()
    {
        var tracker = new CellTracker(new InteriorRegistry());
        var cell = fixture.Block(InteriorLocations.MinesOfDespair.Location.Position.Landblock).Cells.GetCell(InteriorLocations.MinesOfDespair.CellId)!;
        var eye = InteriorLocations.MinesOfDespair.Location.Global;
        tracker.Place(cell, eye);
        Assert.False(tracker.Update(eye));
        Assert.Equal(cell.Id, tracker.CellId);
        Assert.True(tracker.IsInDungeonCell);
    }

    [RequiresDatsFact]
    public void A_jump_out_of_a_dungeon_that_only_moved_the_world_drops_the_stale_cell()
    {
        // A teleport panel that calls WorldRoot.Teleport and moves the camera, without PlaceCamera: the streamer drops
        // every landblock, the dungeon's included, and the camera is elsewhere.
        var dungeon = InteriorLocations.HoltburgRedoubt;
        var coord = dungeon.Location.Position.Landblock;
        var registry = fixture.Registry(coord);
        var tracker = new CellTracker(registry);
        var eye = dungeon.Location.Global with { Z = dungeon.Location.Global.Z + 1.7 };
        tracker.Place(registry.GetCell(dungeon.CellId), eye);
        Assert.False(tracker.Update(eye));
        Assert.True(tracker.IsInDungeonCell);

        registry.Remove(coord);
        tracker.LandblockUnloaded(new LandblockCoord(0x12, 0x34)); // another landblock: nothing changes
        tracker.LandblockUnloaded(coord);
        var holtburg = InteriorsFixture.Global(Holtburg, new Vector3(84f, 7.1f, 96f));
        Assert.True(tracker.Update(holtburg));
        Assert.False(tracker.IsIndoors);
        Assert.Equal(CellTransition.Outside, tracker.LastTransition);

        // PlaceCamera's path: the unload comes first, then the new cell is placed, and it is kept.
        tracker.Place(registry.GetCell(dungeon.CellId) ?? fixture.Block(coord).Cells.GetCell(dungeon.CellId), eye);
        tracker.LandblockUnloaded(coord);
        tracker.Place(fixture.Block(coord).Cells.GetCell(dungeon.CellId), eye);
        Assert.False(tracker.Update(eye));
        Assert.Equal(dungeon.CellId, tracker.CellId);
    }

    private HashSet<uint> BuildingCells(int buildingIndex)
    {
        var block = fixture.Block(Holtburg);
        var statics = LandblockStatics.Read(fixture.Dats, Holtburg)!;
        var building = statics.Buildings[buildingIndex];
        var adjacency = block.Cells.PortalAdjacency();
        var result = new HashSet<uint>();
        var queue = new Queue<uint>(building.Portals.Select(p => p.OtherCellId));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (result.Add(id))
            {
                foreach (var next in adjacency[id])
                {
                    queue.Enqueue(next);
                }
            }
        }

        return result;
    }
}
