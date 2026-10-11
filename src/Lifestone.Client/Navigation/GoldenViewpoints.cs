using System.Globalization;
using Lifestone.Client.World;
using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Navigation;

/// <summary>When a golden viewpoint is shown: a time of day on the first calendar day whose retail day group is
/// <see cref="DayGroup"/>.</summary>
/// <param name="DayFraction">Fraction of the Dereth day (0.5 = Midsong, retail's midday).</param>
/// <param name="DayGroup">The day group (weather) wanted; the date is chosen so retail's own
/// <c>SkyDesc::CalcPresentDayGroup</c> picks it, so the same date shows the same sky in retail.</param>
public sealed record GoldenTime(double DayFraction, int DayGroup);

/// <summary>One fixed view the harness renders, so the same view can be captured in retail and compared.</summary>
/// <param name="Id">File-safe id, also the screenshot's name (e.g. <c>01-holtburg-noon</c>).</param>
/// <param name="Title">What it shows.</param>
/// <param name="Target">The spot looked at: a teleport destination name (ACE point of interest or curated interior), a
/// <c>--start</c> style location (<c>holtburg</c>, <c>A9B4</c>, <c>A9B40019@84,7.1,94</c>, <c>mines-of-despair</c>) or
/// map coordinates (<c>42.1N 33.6E</c>). A target in an EnvCell makes an indoor view (<see cref="GoldenViewpoints.IsIndoor"/>):
/// the camera stands on its floor point at eye height, as the teleport panel puts it there.</param>
/// <param name="Distance">Metres behind the spot (opposite <paramref name="HeadingDegrees"/>); 0 for indoor views.</param>
/// <param name="Height">Metres above the spot; 0 for indoor views (the eye height applies).</param>
/// <param name="HeadingDegrees">Compass heading the camera faces; null = the spot's own facing.</param>
/// <param name="PitchDegrees">Pitch override (positive looks up); null = look at the spot.</param>
/// <param name="Time">When.</param>
/// <param name="Notes">What to compare against retail.</param>
public sealed record GoldenViewpoint(
    string Id,
    string Title,
    string Target,
    double Distance,
    double Height,
    double? HeadingDegrees,
    double? PitchDegrees,
    GoldenTime Time,
    string Notes);

/// <summary>
/// The fixed list of golden viewpoints (M1 definition of done item 11) and the helpers that make each one exactly
/// reproducible: a resolved target, a camera pose and a game time.
/// </summary>
/// <remarks>
/// Day groups in the user's Region (docs/sky.md): 0-2, 4-6 Sunny; 8, 10, 11 Clear; 12-14 Cloudy; 3, 7, 9, 15-19 Rainy.
/// Times: 0.3 Dawnsong (early morning; the sun's arc starts at 0.16), 0.5 Midsong (midday), 0.9 between Gloaming and
/// Gloaming-and-Half (the red dusk keyframe), 0.08 Darktide-and-Half (night: stars, dim clouds).
/// </remarks>
public static class GoldenViewpoints
{
    /// <summary>The first day searched for a viewpoint's day group: Morningthaw 1 of this Portal Year.</summary>
    public const int BaseYear = 10;

    public const int Sunny = 0;
    public const int Rainy = 3;
    public const int Cloudy = 12;

    public static IReadOnlyList<GoldenViewpoint> All { get; } =
    [
        new("01-holtburg-noon", "Holtburg town at midday", "Holtburg", 90, 45, null, null, new(0.5, Sunny),
            "Start view. Compare sky colour, sun-lit terrain, fog colour at the horizon (day fog 150-2,400 m, #C3C8DC)."),
        new("02-holtburg-morning", "Holtburg in the early morning, looking east", "Holtburg", 90, 30, 90, 5, new(0.3, Sunny),
            "Sun sprite low in the east (13.6 degrees up its arc; anchored at height 0, so lower seen from the town); warm FAD797 sun light."),
        new("03-holtburg-dusk", "Holtburg at dusk, looking west", "Holtburg", 90, 30, 270, 5, new(0.9, Sunny),
            "Red dusk fog #5A3C3C at 90-800 m, sun setting in the west, stars fading in."),
        new("04-holtburg-night", "Holtburg at night", "Holtburg", 90, 45, null, null, new(0.08, Sunny),
            "Night: fog 0-400 m #171725, stars layer opaque, clouds at 15-22% light, purple-blue ambient."),
        new("05-holtburg-above", "Holtburg from above at midday", "Holtburg", 300, 450, null, null, new(0.5, Sunny),
            "Town layout, roads and terrain blending from 450 m up; fog reaches the edge of the view."),
        new("06-holtburg-sky", "The midday sky over Holtburg, looking up east", "Holtburg", 0, 2, 90, 55, new(0.5, Sunny),
            "Dome, cloud layer and the sun sprite 75 degrees up its arc in the east (the light comes from 67 degrees)."),
        new("07-holtburg-rain", "Holtburg on a rainy day", "Holtburg", 90, 45, null, null, new(0.5, Rainy),
            "Rainy day group: grey dome, low cloud deck, rain cylinder around the camera, fog 150-1,500 m."),
        new("08-arwic-noon", "Arwic at midday", "Arwic", 90, 45, null, null, new(0.5, Sunny), "Town drop point."),
        new("09-yaraq-noon", "Yaraq (desert) at midday", "Yaraq", 90, 45, null, null, new(0.5, Sunny), "Desert sand, Gharu'ndim town."),
        new("10-shoushi-noon", "Shoushi at midday", "Shoushi", 90, 45, null, null, new(0.5, Sunny), "Sho town."),
        new("11-glenden-wood-noon", "Glenden Wood at midday", "Glenden Wood", 90, 45, null, null, new(0.5, Cloudy),
            "Forest town under a cloudy sky (clouds drawn over the sun in the Cloudy groups)."),
        new("12-snow-noon", "Snowy mountains west of Holtburg (0x7AC2)", "7AC2", 150, 60, 0, null, new(0.5, Sunny),
            "Snow and ice terrain at 224-390 m."),
        new("13-coast-noon", "Coast near Hebian-To (0xE74F)", "E74F", 150, 60, 90, null, new(0.5, Sunny),
            "Shore line, water terrain, fog over the sea."),
        new("14-swamp-noon", "Marsh north-west of Holtburg (0xA8B5)", "A8B5", 150, 50, 0, null, new(0.5, Sunny),
            "Teal marsh water terrain."),

        // Indoor views (EnvCells, docs/interiors.md §6): a building interior and dungeons. The camera stands where the
        // teleport panel puts it (the curated floor point, eyes 1.7 m up). Server-spawned objects (portals, doors,
        // shopkeepers, signs) come from ACE's CreateObject in M2 and are absent in M1.
        new("15-holtburg-shop", "Inside the Holtburg shop, facing the counter", "holtburg-shop", 0, 0, null, -12, new(0.5, Sunny),
            "Building interior (EnvCell 0xA9B40163, seen_outside: the sky's object ambient of the time of day applies, the landscape stays loaded). The purple block on the table is the Grapes static at its Resting placement (faithful to the dats)."),
        new("16-town-network", "The Town Network fountain hall", "town-network", 0, 0, null, -5, new(0.5, Sunny),
            "Dungeon hub (EnvCell 0x00070143, not seen_outside: no landscape or sky, ambient 0.2, burned-in static lights). The portals are server objects (M2)."),
        new("17-mines-of-despair", "Mines of Despair, the entry corridor", "mines-of-despair", 0, 0, null, -5, new(0.5, Sunny),
            "Classic dungeon (EnvCell 0x01880307): timbered mine corridor, barrels and webs, burned-in light; the time of day does not reach it."),
        new("18-holtburg-redoubt", "Holtburg Redoubt, the entry", "holtburg-redoubt", 0, 0, null, -5, new(0.5, Sunny),
            "Classic dungeon near Holtburg (EnvCell 0x016301AC): stone halls, wall sconces, burned-in light."),
    ];

    /// <summary>True when a resolved target is inside an EnvCell (an indoor view: the camera stands on its floor point).</summary>
    public static bool IsIndoor(WorldLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return AcCoordinates.IsEnvCell(location.Position.CellId);
    }

    /// <summary>Finds the viewpoint with this id (or a unique id prefix / name fragment).</summary>
    public static GoldenViewpoint? Find(string idOrName)
    {
        if (All.FirstOrDefault(v => string.Equals(v.Id, idOrName, StringComparison.OrdinalIgnoreCase)) is { } exact)
        {
            return exact;
        }

        var matches = All.Where(v => v.Id.Contains(idOrName, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>
    /// The game time of a <see cref="GoldenTime"/>: the first day from Morningthaw 1, <see cref="BaseYear"/> P.Y. whose
    /// retail day group is the one wanted, at the day fraction.
    /// </summary>
    /// <exception cref="InvalidOperationException">No day within ten years uses that group.</exception>
    public static double GameTimeFor(GoldenTime time, DerethSky sky)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(sky);
        var calendar = sky.Calendar;
        for (var d = 0; d < calendar.DaysPerYear * 10; d++)
        {
            var year = BaseYear + (d / calendar.DaysPerYear);
            var day = d % calendar.DaysPerYear;
            var t = calendar.ToGameTime(year, day, time.DayFraction);
            if (sky.DayGroupIndexAt(t) == time.DayGroup)
            {
                return t;
            }
        }

        throw new InvalidOperationException($"No day in ten years from {BaseYear} P.Y. uses day group {time.DayGroup}.");
    }

    /// <summary>Resolves a target: a teleport destination name, a location argument, or map coordinates.</summary>
    public static bool TryResolveTarget(string target, TeleportCatalog catalog, out WorldLocation location, out string? error)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        error = null;
        var normalized = TeleportCatalog.Normalize(target);
        if (catalog.Destinations.FirstOrDefault(d => TeleportCatalog.Normalize(d.Name) == normalized
                                                     || d.Aliases.Any(a => TeleportCatalog.Normalize(a) == normalized)) is { } destination)
        {
            location = destination.Location;
            return true;
        }

        if (WorldLocations.TryParse(target, out location, out _))
        {
            return true;
        }

        if (MapCoordinateParser.TryParse(target, out var map, out _) && MapCoordinateParser.TryGetCellCentre(map, out var centre))
        {
            location = new WorldLocation(map.ToString(), centre, false, 0.0);
            return true;
        }

        error = $"'{target}' is not a destination, a location or map coordinates";
        location = WorldLocations.Holtburg;
        return false;
    }

    /// <summary>The camera pose for a viewpoint at a resolved spot.</summary>
    public static CameraPose PoseFor(GoldenViewpoint viewpoint, WorldLocation location, Func<GlobalPosition, double?> groundAt)
    {
        ArgumentNullException.ThrowIfNull(viewpoint);
        var spot = CameraPlacement.Spot(location, groundAt);
        var heading = viewpoint.HeadingDegrees ?? location.HeadingDegrees;
        var pose = CameraPlacement.Overview(spot, heading, groundAt, viewpoint.Height, viewpoint.Distance);
        if (viewpoint.Distance <= 0)
        {
            pose = pose with { HeadingDegrees = heading };
        }

        return viewpoint.PitchDegrees is { } pitch ? pose with { PitchDegrees = pitch } : pose;
    }

    /// <summary>ACE's admin teleport command for a camera position (the owner's way to stand there in retail on ACE).</summary>
    public static string AceTeleloc(GlobalPosition eye, double headingDegrees)
    {
        if (!AcCoordinates.TryGetOutdoorCell(eye, out var cell))
        {
            return "-";
        }

        return AceTeleloc(cell, headingDegrees);
    }

    /// <summary>
    /// ACE's admin teleport command for a cell position (any cell: an EnvCell keeps its own id and landblock-local origin,
    /// which ACE's <c>@teleloc</c> takes as given; for an indoor view this is the floor point the camera stands on).
    /// </summary>
    public static string AceTeleloc(CellPosition position, double headingDegrees)
    {
        // Retail Frame::set_heading builds a rotation of -heading about +Z: (w, z) = (cos(-h/2), sin(-h/2)). Rounded,
        // then "+ 0.0" turns a negative zero (heading 0) into 0, so the command never reads "-0.000000".
        var half = -headingDegrees * Math.PI / 360.0;
        var w = Math.Round(Math.Cos(half), 6) + 0.0;
        var z = Math.Round(Math.Sin(half), 6) + 0.0;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"@teleloc 0x{position.CellId:X8} [{position.Local.X:0.000} {position.Local.Y:0.000} {position.Local.Z:0.000}] {w:0.000000} 0 0 {z:0.000000}");
    }
}
