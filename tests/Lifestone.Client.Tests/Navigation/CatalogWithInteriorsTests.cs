using Lifestone.Client.Interiors;
using Lifestone.Client.Navigation;
using Lifestone.Client.World;

namespace Lifestone.Client.Tests.Navigation;

/// <summary>
/// The teleport panel's catalogue (<see cref="TeleportCatalog.Default"/>): ACE's places plus the curated dungeons and
/// building interiors (M1 definition of done item 2: at least three dungeons and a building interior).
/// </summary>
public sealed class CatalogWithInteriorsTests
{
    private static readonly TeleportCatalog Catalog = TeleportCatalog.Default();

    [Fact]
    public void The_panel_lists_dungeons_and_a_building_interior()
    {
        var dungeons = Catalog.Destinations.Where(d => d.Interior == InteriorKind.Dungeon).Select(d => d.Name).ToArray();
        var buildings = Catalog.Destinations.Where(d => d.Interior == InteriorKind.BuildingInterior).Select(d => d.Name).ToArray();
        Assert.True(dungeons.Length >= 3, string.Join(", ", dungeons));
        Assert.NotEmpty(buildings);
        Assert.Equal(
            ["Black Spawn Den (Aluvian)", "Glenden Wood Dungeon", "Holtburg Redoubt", "Mines of Despair", "The Marketplace of Dereth", "Town Network", "Training Academy"],
            dungeons);
        Assert.Equal(["Holtburg shop (interior)", "Holtburg smithy (interior)"], buildings);
        Assert.All(Catalog.Destinations.Where(d => d.Interior is not null), d => Assert.Equal(DestinationKind.Indoor, d.Kind));
    }

    [Fact]
    public void Interiors_at_an_ace_drop_point_merge_into_it()
    {
        // ACE's Town Network and Marketplace portals drop exactly at the curated points: one entry each, ACE's name and
        // position, the curated name and key as aliases.
        Assert.Equal(56 + InteriorLocations.All.Count - 2, Catalog.Destinations.Count);
        var townNetwork = Assert.Single(Catalog.Destinations, d => d.Location.Position.CellId == 0x00070143);
        Assert.Equal("Town Network", townNetwork.Name);
        Assert.Equal(InteriorKind.Dungeon, townNetwork.Interior);
        Assert.Contains("TN", townNetwork.Aliases); // the key "town-network" reads as the name, so it adds no alias
        Assert.Contains("curated interior 'town-network'", townNetwork.Source);
        Assert.Same(townNetwork, Catalog.Find("town-network"));

        var marketplace = Assert.Single(Catalog.Destinations, d => d.Location.Position.CellId == 0x016C01BC);
        Assert.Equal("The Marketplace of Dereth", marketplace.Name);
        Assert.Equal(InteriorKind.Dungeon, marketplace.Interior);
        Assert.Contains("Marketplace", marketplace.Aliases);

        // The four ACE indoor places nobody curated stay plain "indoors" until their cells are read
        // (Ace_indoor_places_take_their_kind_from_the_cells_seen_outside_flag).
        Assert.Equal("indoors", Catalog.Find("Hotel Swank")!.Where);
        Assert.Equal("dungeon", marketplace.Where);
        Assert.Equal("42.1N, 33.6E", Catalog.Find("Holtburg")!.Where);
    }

    [Theory]
    [InlineData("redoubt", "Holtburg Redoubt")]
    [InlineData("mines", "Mines of Despair")]
    [InlineData("despair", "Mines of Despair")]
    [InlineData("training", "Training Academy")]
    [InlineData("shop", "Holtburg shop (interior)")]
    [InlineData("smithy", "Holtburg smithy (interior)")]
    [InlineData("holtburg-shop", "Holtburg shop (interior)")]
    [InlineData("glenden wood dungeon", "Glenden Wood Dungeon")]
    [InlineData("glenden", "Glenden Wood")] // the town (prefix of its name) before the dungeon
    [InlineData("town network", "Town Network")]
    public void The_curated_places_are_found_by_name_and_key(string query, string expected)
    {
        Assert.Equal(expected, Catalog.Find(query)?.Name);
    }

    [Fact]
    public void Typing_a_kind_of_place_lists_every_place_of_that_kind_after_name_matches()
    {
        var dungeons = Catalog.Search("dungeon");
        Assert.Equal(7, dungeons.Count);
        Assert.All(dungeons, d => Assert.Equal(InteriorKind.Dungeon, d.Interior));

        var interiors = Catalog.Search("interior").Select(d => d.Name).ToArray();
        Assert.Equal(["Holtburg shop (interior)", "Holtburg smithy (interior)"], interiors);

        // A name match outranks a kind match: "indoors" finds nothing by name, then every indoor place.
        var indoors = Catalog.Search("indoors");
        Assert.All(indoors, d => Assert.Equal(DestinationKind.Indoor, d.Kind));
        Assert.Equal(Catalog.Destinations.Count(d => d.Kind == DestinationKind.Indoor), indoors.Count);
        Assert.Equal(indoors, Catalog.Search("ind"));
        Assert.Equal(dungeons, Catalog.Search("dun").Where(d => d.Interior == InteriorKind.Dungeon)); // after Xarabydun, a name match
    }

    [Theory]
    [InlineData("in")]
    [InlineData("or")]
    public void A_short_query_does_not_list_places_by_their_kind(string query)
    {
        // Before, the kind was matched as a substring: "do" or "or" appended every "indoors" place, "in" every interior.
        var results = Catalog.Search(query);
        var q = TeleportCatalog.Normalize(query);
        Assert.All(results, d => Assert.Contains(
            d.Aliases.Prepend(d.Name),
            name => TeleportCatalog.Normalize(name).Contains(q, StringComparison.Ordinal)));
        Assert.DoesNotContain(results, d => d.Name == "Hotel Swank!");
    }

    [Fact]
    public void The_jump_message_names_the_kind_once()
    {
        Assert.Equal("Holtburg shop (interior)", Catalog.Find("holtburg-shop")!.Label);
        Assert.Equal("Glenden Wood Dungeon", Catalog.Find("glenden wood dungeon")!.Label);
        Assert.Equal("Town Network (dungeon)", Catalog.Find("town network")!.Label);
        Assert.Equal("Hotel Swank! (indoors)", Catalog.Find("hotel swank")!.Label);
        Assert.Equal("Holtburg", Catalog.Find("holtburg")!.Label);
    }

    [Fact]
    public void Indoor_places_nobody_classified_take_their_kind_from_seen_outside()
    {
        var seen = new Dictionary<uint, bool?>();
        foreach (var d in Catalog.Destinations.Where(d => d.Kind == DestinationKind.Indoor && d.Interior is null))
        {
            seen[d.Location.Position.CellId] = d.Name == "Hotel Swank!" ? true : d.Name == "AC Storage" ? null : false;
        }

        Assert.Equal(4, seen.Count);
        var classified = Catalog.ClassifyIndoor(id => seen.TryGetValue(id, out var s) ? s : throw new InvalidOperationException($"asked for 0x{id:X8}"));
        Assert.Equal(InteriorKind.Dungeon, classified.Find("underground city")!.Interior);
        Assert.Equal(InteriorKind.BuildingInterior, classified.Find("hotel swank")!.Interior);
        Assert.Null(classified.Find("ac storage")!.Interior); // unknown flag: stays "indoors"
        Assert.Contains(classified.Find("underground city")!, classified.Search("dungeon"));
        Assert.Equal(Catalog.Destinations.Count, classified.Destinations.Count);

        // Curated kinds are kept.
        Assert.Equal(InteriorKind.BuildingInterior, classified.Find("holtburg-shop")!.Interior);
    }

    [Fact]
    public void Curated_destinations_keep_the_interiors_tracks_positions()
    {
        foreach (var interior in InteriorLocations.All)
        {
            var d = Catalog.Find(interior.Key);
            Assert.NotNull(d);
            Assert.Equal(interior.CellId, d.Location.Position.CellId);
            Assert.True(System.Numerics.Vector3.Distance(interior.Location.Position.Local, d.Location.Position.Local) <= TeleportCatalog.SamePlaceMetres, interior.Key);
            Assert.True(AcCoordinates.IsEnvCell(d.Location.Position.CellId), interior.Key);
            Assert.Null(d.Map);
        }
    }

    [Fact]
    public void Merging_is_by_place_not_by_name()
    {
        var elsewhere = new TeleportDestination(
            "Town Network",
            [],
            new WorldLocation("Town Network", new CellPosition(0x00070143, new System.Numerics.Vector3(10f, -10f, 0f)), true, 0.0),
            "test");
        var catalog = TeleportCatalog.WithInteriors([elsewhere], [InteriorLocations.TownNetwork]);
        Assert.Equal(2, catalog.Destinations.Count); // 60 m apart: two places of the same name
        Assert.Single(catalog.Destinations, d => d.Interior == InteriorKind.Dungeon);
    }
}
