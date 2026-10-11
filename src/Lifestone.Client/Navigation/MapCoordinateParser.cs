using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.World;

namespace Lifestone.Client.Navigation;

/// <summary>
/// Reads retail map coordinates typed by a person ("42.1N 33.6E") and finds the 24 m landscape cell they name.
/// </summary>
/// <remarks>
/// <para>Retail shows the coordinates of the player's outdoor cell, not of the exact position
/// (<c>CPlayerSystem::InqPlayerCoords</c>, acclient.c:398264: <c>(lcoord - 1024) x 0.1 + 0.5</c>; see
/// <see cref="AcCoordinates.TryGetMapCoordinates(uint, out MapCoordinates)"/>), so each 0.1 step names one cell. This is
/// the inverse: <c>lcoord = round((value - 0.5) / 0.1) + 1024</c>. Values with more decimals are rounded to the nearest
/// 0.1 first (half away from zero), i.e. read as retail would print them.</para>
/// <para>Accepted forms (case and spacing free): <c>42.1N 33.6E</c>, <c>42.1N, 33.6E</c>, <c>33.6E 42.1N</c>,
/// <c>42.1 N 33.6 E</c>, and signed pairs without letters, latitude first like retail's text: <c>42.1, -33.6</c>
/// (negative = south / west).</para>
/// </remarks>
public static partial class MapCoordinateParser
{
    /// <summary>Parses <paramref name="text"/>.</summary>
    public static bool TryParse(string? text, out MapCoordinates coordinates, out string? error)
    {
        coordinates = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "type map coordinates, e.g. 42.1N 33.6E";
            return false;
        }

        double? ns = null;
        double? ew = null;
        var lettered = LetteredValue().Matches(text);
        if (lettered.Count > 0)
        {
            if (lettered.Count != 2 || LetteredValue().Replace(text, " ").Any(c => !char.IsWhiteSpace(c) && c != ','))
            {
                error = $"'{text}': expected one N/S and one E/W value, e.g. 42.1N 33.6E";
                return false;
            }

            foreach (Match m in lettered)
            {
                var value = double.Parse(m.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                switch (char.ToUpperInvariant(m.Groups["d"].Value[0]))
                {
                    case 'N':
                        ns = Set(ns, value);
                        break;
                    case 'S':
                        ns = Set(ns, -value);
                        break;
                    case 'E':
                        ew = Set(ew, value);
                        break;
                    default:
                        ew = Set(ew, -value);
                        break;
                }
            }

            if (ns is null || ew is null || double.IsNaN(ns.Value) || double.IsNaN(ew.Value))
            {
                error = $"'{text}': expected one N/S and one E/W value, e.g. 42.1N 33.6E";
                return false;
            }
        }
        else
        {
            var parts = text.Split([',', ' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            {
                error = $"'{text}': expected e.g. 42.1N 33.6E, or 42.1, 33.6 (latitude first; negative = S/W)";
                return false;
            }

            ns = a;
            ew = b;
        }

        coordinates = new MapCoordinates(RoundToTenth(ns.Value), RoundToTenth(ew.Value));
        if (!TryGetCell(coordinates, out _))
        {
            error = $"'{text}': {coordinates} is off the map (101.9S..102.0N, 101.9W..102.0E)";
            coordinates = default;
            return false;
        }

        return true;
    }

    /// <summary>The outdoor cell retail shows <paramref name="coordinates"/> for, or false off the map.</summary>
    public static bool TryGetCell(MapCoordinates coordinates, out uint cellId)
    {
        var lx = (int)Math.Round((coordinates.EastWest - AcCoordinates.MapCellCentreOffset) / AcCoordinates.MapUnitsPerCell, MidpointRounding.AwayFromZero)
                 + AcCoordinates.MapOriginLandCoord;
        var ly = (int)Math.Round((coordinates.NorthSouth - AcCoordinates.MapCellCentreOffset) / AcCoordinates.MapUnitsPerCell, MidpointRounding.AwayFromZero)
                 + AcCoordinates.MapOriginLandCoord;
        cellId = AcCoordinates.LandCoordToCellId(lx, ly);
        return cellId != 0;
    }

    /// <summary>The centre of the cell <paramref name="coordinates"/> name, at height 0 (the caller finds the ground).</summary>
    public static bool TryGetCellCentre(MapCoordinates coordinates, out CellPosition centre)
    {
        centre = default;
        if (!TryGetCell(coordinates, out var cellId) || !AcCoordinates.TryGetLandCoord(cellId, out var lx, out var ly))
        {
            return false;
        }

        centre = new CellPosition(cellId, new System.Numerics.Vector3(
            (float)(((lx % AcCoordinates.CellsPerLandblock) + 0.5) * AcCoordinates.CellLength),
            (float)(((ly % AcCoordinates.CellsPerLandblock) + 0.5) * AcCoordinates.CellLength),
            0f));
        return true;
    }

    private static double? Set(double? current, double value) => current is null ? value : double.NaN;

    private static double RoundToTenth(double value) => Math.Round(value * 10.0, MidpointRounding.AwayFromZero) / 10.0;

    [GeneratedRegex(@"(?<v>[0-9]+(?:\.[0-9]*)?|\.[0-9]+)\s*(?<d>[NnSsEeWw])(?![A-Za-z])")]
    private static partial Regex LetteredValue();
}
