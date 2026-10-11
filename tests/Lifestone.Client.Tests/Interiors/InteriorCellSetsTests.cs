using System.Collections.Concurrent;
using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Data.Tests;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>Which cells are meshed (retail <c>CLandBlock::init_buildings</c>, <c>CEnvCell::grab_visible_cells</c>).</summary>
public sealed class InteriorCellSetsTests(InteriorsFixture fixture, ITestOutputHelper output) : IClassFixture<InteriorsFixture>
{
    private static readonly LandblockCoord Holtburg = InteriorsFixture.Holtburg;

    [RequiresDatsFact]
    public void Outdoors_only_the_buildings_stab_lists_are_meshed()
    {
        var block = fixture.Block(Holtburg);
        var statics = LandblockStatics.Read(fixture.Dats, Holtburg)!;
        var building = InteriorCellSets.BuildingStabCells(statics.Buildings);
        var drawn = InteriorCellSets.CellsToDraw(block.Cells, building, all: false);
        output.WriteLine($"Holtburg: {block.Cells.Count} cells, {building.Count} in the buildings' stab lists, {drawn.Count} meshed");

        Assert.Equal(building.OrderBy(id => id), block.BuildingCellIds.OrderBy(id => id));
        Assert.True(drawn.Count < block.Cells.Count);
        Assert.All(drawn, id => Assert.Contains(id, building));
        Assert.Equal(drawn.Order(), drawn);

        // Every doorway's own cell is in, and the stone room beside the town (no building) is not.
        Assert.All(statics.Buildings.SelectMany(b => b.Portals), p => Assert.Contains(p.OtherCellId, drawn));
        Assert.DoesNotContain(0xA9B40183u, drawn);
        Assert.False(InteriorCellSets.NeedsAllCells(block.Cells, building, 0));
        Assert.False(InteriorCellSets.NeedsAllCells(block.Cells, building, InteriorLocations.HoltburgSmithy.CellId));
        Assert.False(InteriorCellSets.NeedsAllCells(block.Cells, building, InteriorLocations.HoltburgShop.CellId));
        Assert.True(InteriorCellSets.NeedsAllCells(block.Cells, building, 0xA9B40183));
        Assert.Equal(block.Cells.Ids, InteriorCellSets.CellsToDraw(block.Cells, building, all: true));
    }

    [RequiresDatsFact]
    public void Every_cell_seen_from_outdoors_around_Holtburg_is_meshed()
    {
        // The outdoor walks only enter building doorways and follow their stab lists, so the meshed set suffices.
        var registry = fixture.Registry(Holtburg);
        var block = fixture.Block(Holtburg);
        var tracker = new CellTracker(registry);
        var seen = new HashSet<uint>();
        for (var x = 8f; x < 192f; x += 24f)
        {
            for (var y = 8f; y < 192f; y += 24f)
            {
                var ground = fixture.TerrainHeight(Holtburg, x, y);
                var eye = InteriorsFixture.Global(Holtburg, new Vector3(x, y, ground + 1.7f));
                tracker.Place(null, eye);
                for (var heading = 0; heading < 360; heading += 45)
                {
                    seen.UnionWith(InteriorVisibility.Compute(registry, tracker, eye, InteriorsFixture.Looking(heading, -5)).VisibleCells);
                }
            }
        }

        output.WriteLine($"{seen.Count} cells seen from 64 outdoor points x 8 directions");
        Assert.NotEmpty(seen);
        Assert.All(seen, id => Assert.Contains(id, block.BuildingCellIds));
    }

    [RequiresDatsTheory]
    [InlineData("town-network")]
    [InlineData("training-academy")]
    [InlineData("holtburg-redoubt")]
    public void In_a_dungeon_cell_the_whole_landblock_is_meshed(string key)
    {
        var location = InteriorLocations.All.Single(l => l.Key == key);
        var coord = location.Location.Position.Landblock;
        var block = fixture.Block(coord);
        var statics = LandblockStatics.Read(fixture.Dats, coord);
        var building = InteriorCellSets.BuildingStabCells(statics?.Buildings ?? []);
        Assert.True(InteriorCellSets.NeedsAllCells(block.Cells, building, location.CellId));
        Assert.Equal(block.Cells.Count, InteriorCellSets.CellsToDraw(block.Cells, building, all: true).Count);

        // Another landblock's cell, or outdoors, does not make this one complete.
        Assert.False(InteriorCellSets.NeedsAllCells(block.Cells, building, InteriorLocations.HoltburgShop.CellId));
        Assert.False(InteriorCellSets.NeedsAllCells(block.Cells, building, 0));
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_building_doorway_cone_fits_the_doorway_table()
    {
        // A cone has at most one plane per doorway edge (ViewVolume.Through); the doorway-plane table holds
        // DoorwayRows.MaxConePlanes per row. The edge count depends only on the building model.
        var dats = fixture.Dats;
        var byModel = new ConcurrentDictionary<uint, int>();
        var histogram = new ConcurrentDictionary<int, int>();
        var doorways = 0;
        Parallel.ForEach(CellDiscovery.LandblocksWithInfo(dats), coord =>
        {
            if (LandblockStatics.Read(dats, coord) is not { } statics)
            {
                return;
            }

            foreach (var building in statics.Buildings)
            {
                if (building.Portals.Count == 0)
                {
                    continue;
                }

                var corners = BuildingPortalGeometry.Load(dats, building).Doorways.Select(d => d.Vertices.Length).ToArray();
                byModel.TryAdd(building.ModelId, corners.Max());
                foreach (var n in corners)
                {
                    histogram.AddOrUpdate(n, 1, (_, c) => c + 1);
                    Interlocked.Increment(ref doorways);
                }
            }
        });

        output.WriteLine($"{doorways} doorways: " + string.Join(", ", histogram.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} corners: {kv.Value}")));
        Assert.Equal(16937, doorways);
        var worst = byModel.MaxBy(kv => kv.Value);
        output.WriteLine($"most corners: {worst.Value} (model 0x{worst.Key:X8})");
        Assert.Equal(DoorwayRows.MaxConePlanes, histogram.Keys.Max());
    }
}
