using Lifestone.Client.Interiors;
using Lifestone.Client.Navigation;
using Lifestone.Client.Tests.Interiors;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Navigation;

/// <summary>
/// The main scene's catalogue classifies ACE's indoor points of interest by their cell's seen_outside flag, so the
/// teleport panel and the position HUD name a place the same way (review finding: the panel said "indoors" where the HUD
/// said "indoors (dungeon)", and searching "dungeon" did not list the Underground City).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class CatalogClassificationTests(InteriorsFixture fixture) : IClassFixture<InteriorsFixture>
{
    [RequiresDatsFact]
    public void Ace_indoor_places_take_their_kind_from_the_cells_seen_outside_flag()
    {
        var catalog = TeleportCatalog.Default().ClassifyIndoor(id => EnvCellData.Read(fixture.Dats, id)?.SeenOutside);
        var classified = catalog.Destinations.Where(d => d.Source.Contains("seen_outside", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["AC Storage", "Hotel Swank!", "Night Club", "Underground City"], classified.Select(d => d.Name).Order().ToArray());
        foreach (var d in classified)
        {
            var cell = EnvCellData.Read(fixture.Dats, d.Location.Position.CellId);
            Assert.NotNull(cell);

            // PositionReadout names an EnvCell "indoors (dungeon)" when it is not seen_outside, else "indoors (building)".
            Assert.Equal(cell.SeenOutside ? InteriorKind.BuildingInterior : InteriorKind.Dungeon, d.Interior);
        }

        Assert.Equal(InteriorKind.Dungeon, catalog.Find("underground city")!.Interior);
        Assert.Contains(catalog.Find("underground city")!, catalog.Search("dungeon"));
        Assert.DoesNotContain(catalog.Destinations, d => d.Kind == DestinationKind.Indoor && d.Interior is null);
    }
}
