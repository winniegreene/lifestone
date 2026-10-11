using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.World;
using Lifestone.Game.Core;

namespace Lifestone.Game.World.Interiors;

/// <summary>
/// The interiors' own command-line arguments (after <c>--</c>), for screenshots and debugging. They are registered
/// with their help in <c>Lifestone.Client.Options.OptionCatalog.Interiors</c> (so <see cref="GameOptions"/> knows them)
/// and parsed with the shared tokenizer and value parsers; a value that does not parse is logged and ignored.
/// <c>--interior-engine-checks</c> is a check suite of the core's check mode (GameOptions, Main).
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>--interior-eye=CELL@x,y,z</term><description>Put the eye exactly here (landblock-local; an outdoor or
/// EnvCell id as in <c>--start</c>), instead of <c>--start</c>'s overview placement.</description></item>
/// <item><term>--interior-look=H,P</term><description>Face compass heading H and pitch P (degrees).</description></item>
/// <item><term>--interior-show-all</term><description>Draw every loaded cell (portal visibility off, as F9).</description></item>
/// <item><term>--interior-debug-ambient=X</term><description>Indoor ambient level instead of retail's 0.2 (0 shows the
/// burned-in static lights alone).</description></item>
/// <item><term>--interior-tour[=DIR]</term><description>The measurement tour (<see cref="InteriorTourRunner"/>); DIR
/// defaults to docs/screenshots/m1 (relative paths are taken from the repository root).</description></item>
/// <item><term>--interiors=off</term><description>No interiors (A/B measurements).</description></item>
/// </list>
/// </remarks>
public sealed record InteriorArguments(
    WorldLocation? Eye,
    (double Heading, double Pitch)? Look,
    bool ShowAll,
    float? DebugAmbient,
    bool Off = false,
    string? TourDirectory = null)
{
    public static InteriorArguments Parse(IEnumerable<string> args)
    {
        WorldLocation? eye = null;
        (double, double)? look = null;
        var showAll = false;
        float? ambient = null;
        var off = false;
        string? tour = null;
        var errors = new List<string>();
        foreach (var argument in CommandLineArgument.ParseAll(args))
        {
            var value = argument.Value;
            switch (argument.Name)
            {
                case "--interior-eye":
                    if (WorldLocations.TryParse(value, out var location, out var error))
                    {
                        eye = location;
                    }
                    else
                    {
                        Log.Warn("interiors", "bad --interior-eye", ("problem", error));
                    }

                    break;
                case "--interior-look":
                    var parts = value.Split(',');
                    if (parts.Length == 2
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    {
                        look = (h, p);
                    }
                    else
                    {
                        Log.Warn("interiors", "bad --interior-look (expected heading,pitch)", ("value", value));
                    }

                    break;
                case "--interior-show-all":
                    showAll = true;
                    break;
                case "--interior-debug-ambient":
                    if (OptionValues.Double(argument, errors, double.NaN) is var a && !double.IsNaN(a))
                    {
                        ambient = (float)a;
                    }

                    break;
                case "--interiors":
                    off = !OptionValues.Switch(argument, errors, true);
                    break;
                case "--interior-tour":
                    // infra (M1 wave 3): the default folder is gitignored output, not the working directory.
                    tour = string.IsNullOrWhiteSpace(value) ? "docs/screenshots/m1" : value;
                    break;
            }
        }

        foreach (var error in errors)
        {
            Log.Warn("interiors", "bad argument", ("problem", error));
        }

        return new InteriorArguments(eye, look, showAll, ambient, off, tour);
    }
}
