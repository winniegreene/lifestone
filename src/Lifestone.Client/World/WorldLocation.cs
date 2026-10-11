using System.Globalization;
using System.Numerics;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.World;

/// <summary>A place to start or fly to: a cell position, whether its height is known, and a facing.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Position">Cell id and landblock-local origin.</param>
/// <param name="HasHeight">
/// False when only the landblock or cell was given (the height is 0 and the caller should place the camera relative
/// to the ground); true when <see cref="CellPosition.Local"/>'s Z is a real height.
/// </param>
/// <param name="HeadingDegrees">Compass heading to face (0 = north, 90 = east).</param>
public sealed record WorldLocation(string Name, CellPosition Position, bool HasHeight, double HeadingDegrees)
{
    /// <summary>The position in global metres.</summary>
    public GlobalPosition Global => AcCoordinates.ToGlobal(Position);

    public override string ToString() => $"{Name} {Position}";
}

/// <summary>Named places and the parser for location arguments (<c>--start=</c>, <c>--fly-path=</c>).</summary>
/// <remarks>
/// <para>The town positions are the "Town" entries of ACViewer's <c>ACViewer/Data/Locations.txt</c> (GPL-3.0,
/// @c5c54fb), retail positions in (cell id, landblock-local origin, quaternion w x y z) form:</para>
/// <list type="bullet">
/// <item>Holtburg: <c>0xA9B40019 [84 7.1 94] 0.996917 0 0 -0.078459</c>.</item>
/// <item>Arwic: <c>0xC6A90009 [46.805 4.219 42.005] 1 0 0 0</c>. Landblock 0xC6A9, about 5.9 km east-south-east of
/// Holtburg; map 33.2N, 56.6E by the retail formula.</item>
/// </list>
/// <para>Accepted argument forms (case-insensitive): a name (<c>holtburg</c>); a landblock (<c>A9B4</c>, <c>0xA9B4</c>:
/// its centre); a cell (<c>A9B40019</c>: an outdoor cell's centre); any of these with <c>@x,y,z</c> for an exact
/// landblock-local origin (<c>A9B40019@84,7.1,94</c>).</para>
/// </remarks>
public static class WorldLocations
{
    public static readonly WorldLocation Holtburg = new(
        "Holtburg", new CellPosition(0xA9B40019, new Vector3(84f, 7.1f, 94f)), true, HeadingFromQuaternion(0.996917, -0.078459));

    public static readonly WorldLocation Arwic = new(
        "Arwic", new CellPosition(0xC6A90009, new Vector3(46.805f, 4.219f, 42.005f)), true, HeadingFromQuaternion(1.0, 0.0));

    /// <summary>Every named location, by lower-case name.</summary>
    public static IReadOnlyDictionary<string, WorldLocation> Named { get; } = new Dictionary<string, WorldLocation>(StringComparer.OrdinalIgnoreCase)
    {
        ["holtburg"] = Holtburg,
        ["arwic"] = Arwic,
    };

    /// <summary>
    /// Compass heading of a retail frame whose rotation is about Z only (dat quaternion w, x, y, z with x = y = 0):
    /// the rotation angle is <c>2 atan2(z, w)</c> counter-clockwise, and a heading is measured clockwise from north, so
    /// heading = <c>-2 atan2(z, w)</c> (retail <c>Frame::set_heading</c> builds Euler z = -heading).
    /// </summary>
    public static double HeadingFromQuaternion(double w, double z)
    {
        var heading = -2.0 * Math.Atan2(z, w) * (180.0 / Math.PI);
        heading %= 360.0;
        return heading < 0 ? heading + 360.0 : heading;
    }

    /// <summary>Parses a location argument (see the class remarks).</summary>
    public static bool TryParse(string? text, out WorldLocation location, out string? error)
    {
        location = Holtburg;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty location";
            return false;
        }

        var spec = text.Trim();
        Vector3? exact = null;
        var at = spec.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            var parts = spec[(at + 1)..].Split(',');
            if (parts.Length != 3
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                error = $"'{spec}': expected '@x,y,z' with three numbers";
                return false;
            }

            exact = new Vector3(x, y, z);
            spec = spec[..at];
        }

        // interiors track: the curated dungeons and interiors (Interiors/InteriorLocations.cs) are names too.
        if (Named.TryGetValue(spec, out var named) || Interiors.InteriorLocations.TryGet(spec, out named))
        {
            location = exact is { } local ? named with { Position = named.Position with { Local = local }, HasHeight = true } : named;
            return true;
        }

        var hex = spec.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? spec[2..] : spec;
        if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) || hex.Length is not (4 or 8))
        {
            error = $"'{text}': not a known name ({string.Join(", ", Named.Keys)}), a landblock (A9B4) or a cell id (A9B40019)";
            return false;
        }

        uint cellId;
        Vector3 centre;
        if (hex.Length == 4)
        {
            cellId = (value << 16) | 0xFFFFu;
            centre = new Vector3((float)(AcCoordinates.LandblockLength / 2), (float)(AcCoordinates.LandblockLength / 2), 0f);
        }
        else
        {
            cellId = value;
            if (AcCoordinates.TryGetLandCoord(cellId, out var lx, out var ly))
            {
                centre = new Vector3(
                    (float)(((lx % AcCoordinates.CellsPerLandblock) + 0.5) * AcCoordinates.CellLength),
                    (float)(((ly % AcCoordinates.CellsPerLandblock) + 0.5) * AcCoordinates.CellLength),
                    0f);
            }
            else if (AcCoordinates.IsEnvCell(cellId) && exact is not null)
            {
                centre = Vector3.Zero;
            }
            else
            {
                error = $"'{text}': 0x{cellId:X8} is not an outdoor cell; give an EnvCell an explicit '@x,y,z'";
                return false;
            }
        }

        var landblock = LandblockCoord.FromId(cellId);
        if (!AcCoordinates.IsOnMap(landblock))
        {
            error = $"'{text}': landblock {landblock} is off the map";
            return false;
        }

        location = new WorldLocation(
            $"0x{cellId:X8}",
            new CellPosition(cellId, exact ?? centre),
            exact is not null,
            0.0);
        return true;
    }
}

/// <summary>An automated straight flight between two locations (<c>--fly-path=from:to:speed</c>).</summary>
/// <param name="From">Start.</param>
/// <param name="To">End.</param>
/// <param name="SpeedMetresPerSecond">Ground speed.</param>
public sealed record FlyPathSpec(WorldLocation From, WorldLocation To, double SpeedMetresPerSecond)
{
    /// <summary>Horizontal length of the path in metres.</summary>
    public double HorizontalLength
    {
        get
        {
            var a = From.Global;
            var b = To.Global;
            return Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
        }
    }

    /// <summary>Seconds the flight takes at <see cref="SpeedMetresPerSecond"/>.</summary>
    public double DurationSeconds => HorizontalLength / SpeedMetresPerSecond;

    /// <summary>Compass heading from <see cref="From"/> to <see cref="To"/>.</summary>
    public double HeadingDegrees
    {
        get
        {
            var a = From.Global;
            var b = To.Global;
            return AcCoordinates.HeadingOf(b.X - a.X, b.Y - a.Y);
        }
    }

    /// <summary>Parses <c>from:to:speed</c>, e.g. <c>holtburg:arwic:150</c> or <c>A9B4:C6A9:120</c>.</summary>
    public static bool TryParse(string? text, out FlyPathSpec? spec, out string? error)
    {
        spec = null;
        var parts = (text ?? string.Empty).Split(':');
        if (parts.Length != 3)
        {
            error = $"'{text}': expected from:to:speed";
            return false;
        }

        if (!WorldLocations.TryParse(parts[0], out var from, out error) || !WorldLocations.TryParse(parts[1], out var to, out error))
        {
            return false;
        }

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) || !(speed > 0) || double.IsInfinity(speed))
        {
            error = $"'{parts[2]}': speed must be a positive number of metres per second";
            return false;
        }

        spec = new FlyPathSpec(from, to, speed);
        error = null;
        return true;
    }
}
