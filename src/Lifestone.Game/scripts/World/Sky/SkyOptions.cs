using System.Globalization;
using Lifestone.Client.Options;

namespace Lifestone.Game.World.Sky;

/// <summary>Where the sky objects are anchored (docs/sky.md §4.2).</summary>
public enum SkyAnchor
{
    /// <summary>
    /// Retail: the viewer's landblock south-west corner at height 0 (<c>GameSky::UseTime</c> origin 0,
    /// <c>GameSky::UpdatePosition</c>, <c>PrimD3DRender::positionPushInternal</c>). The sky drifts inside a landblock
    /// and jumps 192 m at every landblock crossing. <c>--retail-sky-anchor</c> or <c>--sky-anchor=retail</c>.
    /// </summary>
    Retail,

    /// <summary>
    /// Non-retail comparison mode: centred on the camera in all three axes (no parallax, no jump, and the sky keeps
    /// its height above the eye however high the camera flies). <c>--sky-anchor=camera</c>.
    /// </summary>
    Camera,

    /// <summary>
    /// The default (render-fidelity fix, lead policy for rendering artifacts): the camera's x and y at retail's
    /// height 0. The sky no longer jumps at landblock edges or drifts inside a landblock, while its height stays where
    /// retail puts it (clouds 750-780 m above sea level, the dome's horizon lower the higher you stand).
    /// <c>--sky-anchor=follow</c>.
    /// </summary>
    Follow,
}

/// <summary>
/// Command-line options of the sky, time and navigation track (read from the user arguments after <c>--</c>; registered
/// with their help in Lifestone.Client's <c>OptionCatalog.Sky</c>).
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>--game-time=S</term><description>Start at game time S seconds (retail calendar; 0 = Morningthaw 1, 10 P.Y., Morntide-and-Half).</description></item>
/// <item><term>--time-of-day=F</term><description>(core option) Day fraction F: of the live (or <c>--game-time</c>) day interactively; of the first
/// Sunny day from 10 P.Y. in automated runs (day group 0 at fraction F, as the core documents it).</description></item>
/// <item><term>--time-speed=X</term><description>Game seconds per real second: 0 pause, 1 retail (the interactive default), 60, 600.</description></item>
/// <item><term>--day-group=N|auto</term><description>Force a Region day group (weather) instead of retail's daily choice.</description></item>
/// <item><term>--sky=off</term><description>Do not draw sky objects (lighting and fog still follow the time).</description></item>
/// <item><term>--sky-anchor=follow|retail|camera</term><description>Sky object anchor (default follow: the camera's x, y at
/// retail's height; <see cref="SkyAnchor"/>).</description></item>
/// <item><term>--retail-sky-anchor</term><description>Retail's landblock-corner anchor (the sky jumps at landblock edges).</description></item>
/// <item><term>--weather=off</term><description>Retail's weather option off (no rain objects).</description></item>
/// <item><term>--ui=on|off|open</term><description>Show or hide the time and teleport panels (default: shown, except in automated runs); <c>open</c> also opens the teleport panel.</description></item>
/// <item><term>--golden-viewpoints[=DIR]</term><description>Render the golden viewpoints to DIR (default docs/screenshots/m1/golden) and quit.</description></item>
/// <item><term>--golden-only=ID[,ID]</term><description>Only these viewpoints (id or unique fragment).</description></item>
/// <item><term>--golden-frames=N</term><description>Frames rendered after streaming is idle before each capture (default 45).</description></item>
/// <item><term>--sky-checks</term><description>Verify byte-space sky blending, sky order and pass layering on the engine, then quit (0 = all hold).</description></item>
/// </list>
/// Automated runs (<c>--screenshot</c>, <c>--fly-path</c>, golden viewpoints, checks) are reproducible whatever the real
/// date: without <c>--game-time</c> they start on the first day of 10 P.Y. whose retail day group is 0 (Sunny), at
/// <c>--time-of-day</c> (default Midsong), and the clock is paused unless <c>--time-speed</c> is given
/// (<see cref="Lifestone.Client.Sky.SkyClockStart"/>).
/// </remarks>
public sealed record SkyOptions
{
    public const string DefaultGoldenDirectory = "docs/screenshots/m1/golden";

    public double? GameTime { get; init; }

    public double? TimeOfDay { get; init; }

    public double? Speed { get; init; }

    public int? DayGroup { get; init; }

    public bool SkyObjects { get; init; } = true;

    public SkyAnchor Anchor { get; init; } = SkyAnchor.Follow;

    public bool Weather { get; init; } = true;

    /// <summary>Show the time and teleport panels; null = shown in interactive runs, hidden in automated ones.</summary>
    public bool? Ui { get; init; }

    /// <summary>Open the teleport panel at start (<c>--ui=open</c>).</summary>
    public bool TeleportOpen { get; init; }

    /// <summary>Golden viewpoints output folder, or null when not requested.</summary>
    public string? GoldenDirectory { get; init; }

    public IReadOnlyList<string> GoldenOnly { get; init; } = [];

    public int GoldenFrames { get; init; } = 45;

    /// <summary>Run <see cref="SkyChecks"/> and quit.</summary>
    public bool SkyChecks { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Parses the track's options with the shared tokenizer and value parsers (<see cref="CommandLineArgument"/>,
    /// <see cref="OptionValues"/>): a value that does not parse is added to <see cref="Errors"/> (SkyTrack logs it) and
    /// the option keeps its default. Every option is registered in Lifestone.Client's <c>OptionCatalog.Sky</c>, which is
    /// how the core's <c>GameOptions</c> knows them.
    /// </summary>
    public static SkyOptions Parse(IEnumerable<string> args)
    {
        var o = new SkyOptions();
        var errors = new List<string>();
        foreach (var argument in CommandLineArgument.ParseAll(args))
        {
            switch (argument.Name)
            {
                case "--game-time":
                    o = o with { GameTime = Number(argument, errors) ?? o.GameTime };
                    break;
                case "--time-of-day":
                    o = o with { TimeOfDay = Number(argument, errors) is { } f ? Math.Clamp(f, 0.0, Math.BitDecrement(1.0)) : o.TimeOfDay };
                    break;
                case "--time-speed":
                    o = o with { Speed = Number(argument, errors) ?? o.Speed };
                    break;
                case "--day-group":
                    if (argument.Value is "" or "auto")
                    {
                        o = o with { DayGroup = null };
                    }
                    else if (int.TryParse(argument.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) && g >= 0)
                    {
                        o = o with { DayGroup = g };
                    }
                    else
                    {
                        errors.Add($"--day-group: '{argument.Value}' is not a day group index or 'auto'");
                    }

                    break;
                case "--sky":
                    o = o with { SkyObjects = OptionValues.Switch(argument, errors, o.SkyObjects) };
                    break;
                case "--sky-anchor":
                    o = o with { Anchor = OptionValues.Choice(argument, errors, o.Anchor, ("follow", SkyAnchor.Follow), ("retail", SkyAnchor.Retail), ("camera", SkyAnchor.Camera)) };
                    break;
                case "--retail-sky-anchor": // render-fidelity fix (wave 3): retail's landblock-corner anchor
                    o = o with { Anchor = SkyAnchor.Retail };
                    break;
                case "--weather":
                    o = o with { Weather = OptionValues.Switch(argument, errors, o.Weather) };
                    break;
                case "--ui":
                    switch (argument.Value)
                    {
                        case "open":
                            o = o with { Ui = true, TeleportOpen = true };
                            break;
                        case "" or "on" or "true" or "yes" or "1":
                            o = o with { Ui = true, TeleportOpen = false };
                            break;
                        case "off" or "false" or "no" or "0":
                            o = o with { Ui = false, TeleportOpen = false };
                            break;
                        default:
                            errors.Add($"--ui: '{argument.Value}' is not on, off or open");
                            break;
                    }

                    break;
                case "--golden-viewpoints":
                    o = o with { GoldenDirectory = argument.Value.Length == 0 ? DefaultGoldenDirectory : argument.Value };
                    break;
                case "--golden-only":
                    o = o with { GoldenOnly = argument.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) };
                    break;
                case "--sky-checks":
                    o = o with { SkyChecks = true };
                    break;
                case "--golden-frames":
                    o = o with { GoldenFrames = Math.Clamp(OptionValues.Int(argument, errors, o.GoldenFrames), 1, 1000) };
                    break;
            }
        }

        return o with { Errors = errors };
    }

    /// <summary>A finite number, or null (reported) when the value does not parse.</summary>
    private static double? Number(CommandLineArgument argument, List<string> errors)
    {
        var v = OptionValues.Double(argument, errors, double.NaN);
        return double.IsNaN(v) ? null : v;
    }
}
