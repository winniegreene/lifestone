using System.Globalization;
using Lifestone.Client.Options;

namespace Lifestone.Client.Audio;

/// <summary>
/// The Audio option group's values (docs/portal-space.md §12; registered with their help in <c>OptionCatalog.Audio</c>), as
/// the game's audio service parses them (<see cref="Parse(IEnumerable{string})"/>) and turns them into the master bus's
/// <see cref="AudioSettings"/> (<see cref="Resolve"/>).
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>--mute</term><description>Silence all sound.</description></item>
/// <item><term>--volume=0..1</term><description>Master volume, linear (default 1).</description></item>
/// <item><term>--record-audio=FILE</term><description>Record the game's own sound to FILE, a <c>.wav</c> under
/// <c>cache/</c> (<see cref="RecordPath"/>; registered in <c>OptionCatalog.Video</c>, the main scene only).</description></item>
/// </list>
/// Owner decision D8: automated runs (a screenshot, a check suite, a scripted capture: any run that ends by itself) are
/// silent unless <c>--volume</c> is given, so a harness never makes noise on the owner's desk. <c>--mute</c> wins over
/// <c>--volume</c>.
/// </remarks>
public sealed record AudioOptions
{
    /// <summary><c>--mute</c>.</summary>
    public bool Mute { get; init; }

    /// <summary><c>--volume</c>, a linear value from 0 to 1; null when not given.</summary>
    public float? Volume { get; init; }

    /// <summary>
    /// <c>--record-audio</c>: where the game's own sound is recorded, a relative path under <c>cache/</c> ending in <c>.wav</c>
    /// (retail sounds are derived data, never committed); null when not given. The Master bus is recorded before its level, so
    /// a silent automated run (D8) records too, and nothing reaches the speakers.
    /// </summary>
    public string? RecordPath { get; init; }

    /// <summary>The problems <see cref="Parse(IEnumerable{string})"/> found, one line each; the option keeps its default.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// The master bus's settings: muted by <c>--mute</c>; else at the given volume; else silent in an automated run (D8) and at
    /// full volume in an interactive one.
    /// </summary>
    /// <param name="automated">True when the run ends by itself (<c>GameOptions.IsAutomated</c>).</param>
    public AudioSettings Resolve(bool automated) =>
        new(Volume ?? 1f, Mute || (automated && Volume is null));

    /// <summary>
    /// Parses the group's options with the shared tokenizer: a <c>--volume</c> that is not a number from 0 to 1, or a
    /// <c>--record-audio</c> that is not a relative <c>.wav</c> path under <c>cache/</c>, is added to <see cref="Errors"/> and
    /// leaves the default. Other groups' arguments are skipped.
    /// </summary>
    public static AudioOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new AudioOptions();
        var errors = new List<string>();
        foreach (var argument in CommandLineArgument.ParseAll(args))
        {
            switch (argument.Name)
            {
                case "--mute":
                    options = options with { Mute = true };
                    break;
                case "--volume":
                    if (float.TryParse(argument.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var volume) && float.IsFinite(volume) && volume is >= 0f and <= 1f)
                    {
                        options = options with { Volume = volume };
                    }
                    else
                    {
                        errors.Add($"--volume: '{argument.Value}' is not a number from 0 to 1");
                    }

                    break;
                case "--record-audio":
                    if (IsCacheWavePath(argument.Value))
                    {
                        options = options with { RecordPath = argument.Value!.Replace('\\', '/') };
                    }
                    else
                    {
                        errors.Add($"--record-audio: '{argument.Value}' is not a relative .wav path under cache/");
                    }

                    break;
            }
        }

        return options with { Errors = errors };
    }

    /// <summary>
    /// True for a relative path whose first folder is <c>cache</c>, with no <c>.</c> or <c>..</c> step, ending in <c>.wav</c>, and with no
    /// Windows device name as a step (<c>cache/NUL.wav</c> is the null device, not a file).
    /// </summary>
    private static bool IsCacheWavePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var steps = path.Replace('\\', '/').Split('/');
        return steps.Length >= 2
            && steps[0] == "cache"
            && steps.All(step => step.Length > 0 && step is not "." and not ".." && !IsDeviceName(step))
            && steps[^1].EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            && steps[^1].Length > ".wav".Length;
    }

    /// <summary>CON, PRN, AUX, NUL, COM1-9 and LPT1-9, with or without an extension, in any case.</summary>
    private static bool IsDeviceName(string step)
    {
        var stem = step.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9');
    }
}
