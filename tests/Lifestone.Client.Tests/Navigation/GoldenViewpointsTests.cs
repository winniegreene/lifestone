using Lifestone.Client.Interiors;
using Lifestone.Client.Navigation;
using Lifestone.Client.Tests.Sky;
using Lifestone.Client.World;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Navigation;

/// <summary>The golden viewpoint list is well-formed and every view is exactly reproducible.</summary>
public sealed class GoldenViewpointsTests(SkyDatsFixture fixture) : IClassFixture<SkyDatsFixture>
{
    private static readonly TeleportCatalog Catalog = TeleportCatalog.FromAce();

    [Fact]
    public void Ids_are_unique_ordered_file_names()
    {
        var ids = GoldenViewpoints.All.Select(v => v.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(ids.OrderBy(i => i, StringComparer.Ordinal), ids);
        Assert.All(ids, id => Assert.Matches("^[0-9]{2}-[a-z0-9-]+$", id));
        Assert.InRange(ids.Length, 12, 20);
        Assert.Equal("01-holtburg-noon", GoldenViewpoints.Find("01")!.Id);
        Assert.Equal("09-yaraq-noon", GoldenViewpoints.Find("yaraq")!.Id);
        Assert.Null(GoldenViewpoints.Find("holtburg")); // ambiguous fragment
    }

    [Fact]
    public void Every_target_resolves_to_a_place_on_the_map()
    {
        foreach (var v in GoldenViewpoints.All)
        {
            Assert.True(GoldenViewpoints.TryResolveTarget(v.Target, Catalog, out var location, out var error), $"{v.Id}: {error}");
            Assert.True(
                AcCoordinates.IsOutdoorCell(location.Position.CellId)
                || location.Position.CellId == ((location.Position.CellId & 0xFFFF0000u) | 0xFFFFu)
                || GoldenViewpoints.IsIndoor(location),
                v.Id);
            Assert.True(AcCoordinates.IsOnMap(location.Position.Landblock), v.Id);

            // The panel's catalogue resolves every target to the same place.
            Assert.True(GoldenViewpoints.TryResolveTarget(v.Target, TeleportCatalog.Default(), out var viaPanel, out error), $"{v.Id}: {error}");
            Assert.Equal(location.Position.CellId, viaPanel.Position.CellId);
        }

        Assert.True(GoldenViewpoints.TryResolveTarget("42.1N 33.6E", Catalog, out var map, out _));
        Assert.Equal(0xA9B40019u, map.Position.CellId);
        Assert.False(GoldenViewpoints.TryResolveTarget("nowhere at all", Catalog, out _, out _));
    }

    [Fact]
    public void Indoor_views_cover_a_building_interior_and_dungeons()
    {
        // Audit finding golden-no-indoor-views: at least a building interior and two dungeons, standing where the
        // teleport panel puts the camera (the curated floor points), no stand-off distance.
        var kinds = new List<InteriorKind>();
        foreach (var v in GoldenViewpoints.All)
        {
            Assert.True(GoldenViewpoints.TryResolveTarget(v.Target, Catalog, out var location, out _), v.Id);
            if (!GoldenViewpoints.IsIndoor(location))
            {
                continue;
            }

            var curated = Assert.Single(InteriorLocations.All, i => i.CellId == location.Position.CellId);
            kinds.Add(curated.Kind);
            Assert.Equal((0.0, 0.0), (v.Distance, v.Height));
            Assert.Equal(curated.Location.Position, location.Position);
        }

        Assert.Contains(InteriorKind.BuildingInterior, kinds);
        Assert.True(kinds.Count(k => k == InteriorKind.Dungeon) >= 2);
        Assert.True(kinds.Count >= 3);
    }

    [Fact]
    public void Indoor_teleloc_names_the_cell_and_its_landblock_local_floor_point()
    {
        var mines = InteriorLocations.MinesOfDespair.Location;
        Assert.Equal("@teleloc 0x01880307 [30.000 -70.000 0.000] 1.000000 0 0 0.000000", GoldenViewpoints.AceTeleloc(mines.Position, 0.0));
        // As a global point (y = -70 in its landblock) it would name the neighbouring landblock's land cell instead.
        Assert.StartsWith("@teleloc 0x0187", GoldenViewpoints.AceTeleloc(mines.Global, 0.0), StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void Each_view_gets_a_date_whose_retail_day_group_is_the_one_wanted()
    {
        foreach (var v in GoldenViewpoints.All)
        {
            var t = GoldenViewpoints.GameTimeFor(v.Time, fixture.Sky);
            var date = fixture.Sky.Calendar.ToDate(t);
            Assert.Equal(v.Time.DayGroup, fixture.Sky.DayGroupIndexAt(t));
            Assert.Equal(v.Time.DayFraction, date.DayFractionExact, 9);
            Assert.True(date.Year >= GoldenViewpoints.BaseYear);
        }

        Assert.Equal("Sunny", fixture.Sky.Sky.DayGroups[GoldenViewpoints.Sunny].Name);
        Assert.Equal("Rainy", fixture.Sky.Sky.DayGroups[GoldenViewpoints.Rainy].Name);
        Assert.Equal("Cloudy", fixture.Sky.Sky.DayGroups[GoldenViewpoints.Cloudy].Name);
    }

    [Fact]
    public void Compass_directions_in_titles_match_the_map()
    {
        // The owner finds these spots in retail by their titles: "<direction> of <place>" must point the right way
        // (within half an octant of the bearing from the place to the target).
        var directions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["north"] = 0, ["north-east"] = 45, ["east"] = 90, ["south-east"] = 135,
            ["south"] = 180, ["south-west"] = 225, ["west"] = 270, ["north-west"] = 315,
            ["N"] = 0, ["NE"] = 45, ["E"] = 90, ["SE"] = 135, ["S"] = 180, ["SW"] = 225, ["W"] = 270, ["NW"] = 315,
            ["NNE"] = 22.5, ["ENE"] = 67.5, ["ESE"] = 112.5, ["SSE"] = 157.5, ["SSW"] = 202.5, ["WSW"] = 247.5, ["WNW"] = 292.5, ["NNW"] = 337.5,
        };
        var pattern = new System.Text.RegularExpressions.Regex(@"\b(?<dir>[A-Za-z-]+) of (?<place>[A-Z][\w' -]*?)(?: \(|$)");
        var checkedTitles = 0;
        foreach (var v in GoldenViewpoints.All)
        {
            var match = pattern.Match(v.Title);
            if (!match.Success || !directions.TryGetValue(match.Groups["dir"].Value, out var stated))
            {
                continue; // no "<direction> of <place>" (a camera direction such as "looking east" is not checked)
            }

            Assert.True(GoldenViewpoints.TryResolveTarget(match.Groups["place"].Value, Catalog, out var place, out var error), $"{v.Id}: {error}");
            Assert.True(GoldenViewpoints.TryResolveTarget(v.Target, Catalog, out var target, out error), $"{v.Id}: {error}");
            var bearing = AcCoordinates.HeadingOf(target.Global.X - place.Global.X, target.Global.Y - place.Global.Y);
            var off = Math.Abs(((bearing - stated + 540.0) % 360.0) - 180.0);
            Assert.True(off <= 22.5, $"{v.Id} '{v.Title}': the target lies at {bearing:0.0}° from {match.Groups["place"].Value}, {off:0.0}° off '{match.Groups["dir"].Value}'");
            checkedTitles++;
        }

        Assert.True(checkedTitles >= 2);
    }

    [Fact]
    public void Poses_follow_the_viewpoint()
    {
        var holtburg = GoldenViewpoints.All[0];
        var pose = GoldenViewpoints.PoseFor(holtburg, WorldLocations.Holtburg, _ => 90.0);
        Assert.Equal(WorldLocations.Holtburg.Global.Z + 45.0, pose.Eye.Z, 4);
        Assert.InRange(pose.PitchDegrees, -27, -26);

        var sky = GoldenViewpoints.Find("06")!;
        var up = GoldenViewpoints.PoseFor(sky, WorldLocations.Holtburg, _ => 90.0);
        Assert.Equal((90.0, 55.0), (up.HeadingDegrees, up.PitchDegrees));
    }

    [Fact]
    public void Ace_teleloc_matches_retails_heading_convention()
    {
        // Holtburg's drop point: @teleloc 0xA9B40019 [84 7.1 94] 0.996917 0 0 -0.078459 (ACViewer Locations.txt).
        var text = GoldenViewpoints.AceTeleloc(WorldLocations.Holtburg.Global, WorldLocations.Holtburg.HeadingDegrees);
        Assert.Equal("@teleloc 0xA9B40019 [84.000 7.100 94.000] 0.996917 0 0 -0.078459", text);
        Assert.Equal("-", GoldenViewpoints.AceTeleloc(new GlobalPosition(-5, 0, 0), 0));
    }
}
