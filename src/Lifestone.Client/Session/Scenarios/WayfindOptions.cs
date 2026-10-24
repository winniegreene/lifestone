using System.Globalization;
using System.Numerics;
using Lifestone.Client.Options;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// A place the <c>wayfind</c> scenario names on the command line (<c>--to=0xA5B00032</c>, <c>--from=0xA9B40019@84,7.7,94</c>): a cell and, optionally, a position inside it
/// (landblock-local metres x, y, z). Without a position an outdoor cell means its centre at the terrain's height (the scenario reads it from the tile once attached); an indoor
/// cell needs one.
/// </summary>
/// <param name="CellId">The cell (outdoor 0x0001 to 0x0040 or an EnvCell), on the map.</param>
/// <param name="Position">The landblock-local position, or null for an outdoor cell's centre.</param>
public readonly record struct WayfindPlace(uint CellId, Vector3? Position)
{
    /// <summary>
    /// Reads <c>&lt;cell&gt;[@x,y,z]</c>: the cell in hex (<c>0x</c> optional, eight digits), the position as three invariant-culture numbers.
    /// </summary>
    /// <param name="text">The option's value.</param>
    /// <param name="place">The place when it parsed.</param>
    /// <param name="problem">Why not, when it did not (the value is not repeated).</param>
    public static bool TryParse(string text, out WayfindPlace place, out string? problem)
    {
        place = default;
        problem = null;
        var at = text.IndexOf('@', StringComparison.Ordinal);
        var cellText = (at < 0 ? text : text[..at]).Trim();
        if (cellText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            cellText = cellText[2..];
        }

        if (cellText.Length != 8 || !uint.TryParse(cellText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var cell))
        {
            problem = "needs a cell id of eight hex digits (0xA5B00032), optionally followed by @x,y,z";
            return false;
        }

        if (!LandDefs.IsOutdoorCell(cell) && !LandDefs.IsEnvCell(cell))
        {
            problem = "is not an outdoor cell (0001 to 0040 in the low word) or an EnvCell (0100 to FFFD)";
            return false;
        }

        if ((cell >> 24) >= LandDefs.BlocksPerSide || ((cell >> 16) & 0xFF) >= LandDefs.BlocksPerSide)
        {
            problem = "names a landblock off the map";
            return false;
        }

        Vector3? position = null;
        if (at >= 0)
        {
            var parts = text[(at + 1)..].Split(',');
            if (parts.Length != 3
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
                || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            {
                problem = "needs three numbers after the @ (x,y,z in landblock-local metres)";
                return false;
            }

            position = new Vector3(x, y, z);
        }
        else if (!LandDefs.IsOutdoorCell(cell))
        {
            problem = "is an indoor cell and needs a position (0xA9B4013F@x,y,z); the scenario does not guess a room's floor (TODO(wayfind-indoor-default))";
            return false;
        }

        place = new WayfindPlace(cell, position);
        return true;
    }

    /// <summary>The place as the option writes it.</summary>
    public override string ToString() => Position is { } p
        ? string.Create(CultureInfo.InvariantCulture, $"0x{CellId:X8}@{p.X:0.##},{p.Y:0.##},{p.Z:0.##}")
        : string.Create(CultureInfo.InvariantCulture, $"0x{CellId:X8}");
}

/// <summary>
/// The wayfind scenario's options (P5a S6; <see cref="WayfindScenario"/>): <c>--to</c> (required by the scenario), <c>--from</c> and <c>--wayfind-back</c>. A value that does not
/// parse is reported in <see cref="Errors"/> (the value is not repeated) and the option keeps its default.
/// </summary>
public sealed record WayfindOptions
{
    /// <summary>Where the run goes (<c>--to</c>), or null.</summary>
    public WayfindPlace? To { get; init; }

    /// <summary>Where the run starts (<c>--from</c>): a player more than <see cref="WayfindScenario.FromToleranceMetres"/> from it is placed there by one mod <c>place</c>; null starts where the login left the player.</summary>
    public WayfindPlace? From { get; init; }

    /// <summary>True (<c>--wayfind-back</c>): after the run out, run back to where the run started.</summary>
    public bool Back { get; init; }

    /// <summary>Every problem found, one line each, values never repeated.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Reads the wayfinder's options out of the parsed arguments.</summary>
    public static WayfindOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new WayfindOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            switch (argument.Name)
            {
                case "--to" or "--from":
                    if (WayfindPlace.TryParse(argument.Value, out var place, out var problem))
                    {
                        o = argument.Name == "--to" ? o with { To = place } : o with { From = place };
                    }
                    else
                    {
                        errors.Add($"{argument.Name}: {problem}; the value is not shown");
                    }

                    break;
                case "--wayfind-back":
                    o = o with { Back = OptionValues.Switch(argument, errors, o.Back) };
                    break;
            }
        }

        return o with { Errors = errors };
    }
}
