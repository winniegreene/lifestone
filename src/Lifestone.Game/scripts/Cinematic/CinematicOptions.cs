using System.Globalization;
using Lifestone.Client.Options;

namespace Lifestone.Game.Cinematic;

/// <summary>
/// Command-line options of the cinematic directors (docs/hero-video.md), read in the main scene and the spell viewer;
/// registered with their help in Lifestone.Client's <c>OptionCatalog.Cinematic</c>.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>--cinematic=FILE</term><description>Film the shots of a shot list (relative to the repository root), then quit. Record with <c>--cinematic-capture</c> (or Godot's Movie Maker, <c>--write-movie</c>) and the engine's <c>--fixed-fps 60</c>; the log names each shot's frames.</description></item>
/// <item><term>--cinematic-capture=DIR</term><description>Write each recorded shot's frames to DIR/&lt;shot&gt;.rgba (raw RGBA8 at the window's size, with a .json sidecar); run with the engine's <c>--fixed-fps</c> (tools\hero-video.ps1 does).</description></item>
/// <item><term>--cinematic-shot=ID[,ID]</term><description>Only these shots (the run's scene films its own kind: world shots in the main scene, spell shots in the spell viewer).</description></item>
/// <item><term>--cinematic-stills[=DIR]</term><description>Instead of filming, save PNG stills of each shot to DIR (default docs/screenshots/hero/stills) for framing.</description></item>
/// <item><term>--cinematic-stills-at=F[,F]</term><description>Where in each shot the stills are taken, as fractions of its length (default 0,0.5,1).</description></item>
/// </list>
/// </remarks>
public sealed record CinematicOptions
{
    public const string DefaultStillsDirectory = "docs/screenshots/hero/stills";

    /// <summary>The shot list file, or null when no cinematic was asked for.</summary>
    public string? ShotListPath { get; init; }

    /// <summary>Folder for the raw frames of each recorded shot (<see cref="FrameRecorder"/>), or null (Movie Maker or a preview).</summary>
    public string? CaptureDirectory { get; init; }

    /// <summary>Shot ids to film (empty = every shot of the run's scene).</summary>
    public IReadOnlyList<string> ShotIds { get; init; } = [];

    /// <summary>Stills folder, or null to film.</summary>
    public string? StillsDirectory { get; init; }

    /// <summary>Fractions of each shot at which stills are taken.</summary>
    public IReadOnlyList<double> StillsAt { get; init; } = [0.0, 0.5, 1.0];

    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>True when the run films (or stills) a shot list.</summary>
    public bool Requested => ShotListPath is not null;

    /// <summary>Parses the cinematic options with the shared tokenizer and value parsers; problems go to <see cref="Errors"/>.</summary>
    public static CinematicOptions Parse(IEnumerable<string> args)
    {
        var o = new CinematicOptions();
        var errors = new List<string>();
        foreach (var argument in CommandLineArgument.ParseAll(args))
        {
            switch (argument.Name)
            {
                case "--cinematic":
                    if (argument.Value.Length == 0)
                    {
                        errors.Add("--cinematic: give the shot list file (--cinematic=FILE)");
                    }
                    else
                    {
                        o = o with { ShotListPath = argument.Value };
                    }

                    break;
                case "--cinematic-capture":
                    if (argument.Value.Length == 0)
                    {
                        errors.Add("--cinematic-capture: give the folder for the frames (--cinematic-capture=DIR)");
                    }
                    else
                    {
                        o = o with { CaptureDirectory = argument.Value };
                    }

                    break;
                case "--cinematic-shot":
                    o = o with { ShotIds = argument.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) };
                    if (o.ShotIds.Count == 0)
                    {
                        errors.Add("--cinematic-shot: give one or more shot ids");
                    }

                    break;
                case "--cinematic-stills":
                    o = o with { StillsDirectory = argument.Value.Length == 0 ? DefaultStillsDirectory : argument.Value };
                    break;
                case "--cinematic-stills-at":
                    var fractions = new List<double>();
                    foreach (var part in argument.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f is >= 0 and <= 1)
                        {
                            fractions.Add(f);
                        }
                        else
                        {
                            errors.Add($"--cinematic-stills-at: '{part}' is not a fraction between 0 and 1");
                        }
                    }

                    if (fractions.Count > 0)
                    {
                        o = o with { StillsAt = fractions };
                    }

                    break;
            }
        }

        return o with { Errors = errors };
    }
}
