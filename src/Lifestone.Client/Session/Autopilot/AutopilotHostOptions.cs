using System.Globalization;
using System.Text;
using Lifestone.Client.Options;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// The hosts' own autopilot options (M5 item 4 h3, D58): the gate's scripted hand-off (<c>--autopilot-test-interrupt-at</c>, <c>--autopilot-test-handback-after</c>; the game's play host and <c>--login-check=autopilot</c> only) and where the headless
/// host keeps the checkpoint (<c>--autopilot-checkpoint-dir</c>). Parsed apart from <see cref="AutopilotOptions"/> (the loop's own options, which a hand-back re-reads) because nothing here changes what the loop does. Immutable; bad values are
/// named in <see cref="Errors"/> and never silently replaced.
/// </summary>
public sealed record AutopilotHostOptions
{
    /// <summary>The longest wait either test option accepts, in seconds: a day (Lifestone's own).</summary>
    public const double MaxTestSeconds = 86_400;

    /// <summary>Seconds after the first fight at which /stop is typed into the chat (<c>--autopilot-test-interrupt-at</c>; a movement key before M5 7 D145); null: none.</summary>
    public double? InterruptAtSeconds { get; init; }

    /// <summary>Seconds after the scripted /stop at which /auto is typed (<c>--autopilot-test-handback-after</c>; Num Lock before M5 7 D145); null: never.</summary>
    public double? HandBackAfterSeconds { get; init; }

    /// <summary>M5 7 D145: <c>--autopilot-control-drill=DIR</c>, the control drill's frame folder (<see cref="ControlDrill"/>; the play host only); null: no drill.</summary>
    public string? ControlDrillDirectory { get; init; }

    /// <summary>The headless host's checkpoint folder (<c>--autopilot-checkpoint-dir</c>); null: the default under the run's output folder.</summary>
    public string? CheckpointDirectory { get; init; }

    /// <summary>
    /// <c>--autopilot-start</c> (M5 item 7 s1, <c>m5-autopilot-start</c>; the play host only): the dial starts once by itself when the character is in the world and the host's own rule allows it (BSD Mage), and the game logs off and quits
    /// when the dial ends by its own stop rule or a failure (<see cref="AutopilotUnattendedEnd"/>). Default off.
    /// </summary>
    public bool StartAtLogin { get; init; }

    /// <summary>
    /// <c>--autopilot-stop-file=PATH</c> (M5 7 F18; the play host only): a clean stop for the soak from outside. The host looks for the file every <see cref="StopFileWatch.PollSeconds"/> s; when it exists the dial is asked to stop
    /// (<see cref="AutopilotHostLogic.StopFileReason"/>), which with <c>--autopilot-start</c> ends the run as the unattended end does. The path is the output-path rule's (relative to the repository; never under src/ or tests/). Null: none.
    /// </summary>
    public string? StopFile { get; init; }

    /// <summary>The parse errors (empty when every option parsed).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>True when the scripted hand-off is asked for.</summary>
    public bool HasHandOff => InterruptAtSeconds is not null;

    /// <summary>The options of the command line.</summary>
    /// <param name="args">The parsed arguments.</param>
    public static AutopilotHostOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new AutopilotHostOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            var known = errors.Count;
            switch (argument.Name)
            {
                case "--autopilot-test-interrupt-at" or "--autopilot-test-handback-after":
                    var seconds = OptionValues.Double(argument, errors, double.NaN);
                    if (errors.Count == known && seconds is >= 0 and <= MaxTestSeconds)
                    {
                        o = argument.Name == "--autopilot-test-interrupt-at" ? o with { InterruptAtSeconds = seconds } : o with { HandBackAfterSeconds = seconds };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 to {MaxTestSeconds:0} seconds"));
                    }

                    break;
                case "--autopilot-start":
                    o = o with { StartAtLogin = OptionValues.Switch(argument, errors, o.StartAtLogin) };
                    break;
                case "--autopilot-stop-file":
                    if (string.IsNullOrWhiteSpace(argument.Value))
                    {
                        errors.Add($"{argument.Name}: needs a file path");
                    }
                    else
                    {
                        o = o with { StopFile = argument.Value.Trim() };
                    }

                    break;
                case "--autopilot-control-drill":
                    if (string.IsNullOrWhiteSpace(argument.Value))
                    {
                        errors.Add($"{argument.Name}: needs a folder (--name=VALUE)");
                    }
                    else
                    {
                        o = o with { ControlDrillDirectory = argument.Value.Trim() };
                    }

                    break;
                case "--autopilot-checkpoint-dir":
                    if (string.IsNullOrWhiteSpace(argument.Value))
                    {
                        errors.Add($"{argument.Name}: needs a folder");
                    }
                    else
                    {
                        o = o with { CheckpointDirectory = argument.Value.Trim() };
                    }

                    break;
            }
        }

        if (o.HandBackAfterSeconds is not null && o.InterruptAtSeconds is null)
        {
            errors.Add("--autopilot-test-handback-after: needs --autopilot-test-interrupt-at (the hand-back follows the interrupt)");
        }

        return o with { Errors = errors };
    }

    /// <summary>
    /// The checkpoint file's name for <paramref name="character"/>: lower case, every run of characters that are not letters or digits one hyphen, plus <c>.json</c> (<c>BSD Mage</c> is <c>bsd-mage.json</c>). Never empty.
    /// </summary>
    /// <param name="character">The character's name.</param>
    public static string CheckpointFileName(string character)
    {
        var sb = new StringBuilder();
        foreach (var c in character ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var stem = sb.ToString().Trim('-');
        return (stem.Length == 0 ? "character" : stem) + ".json";
    }

    /// <summary>The hand-off as the run logs it.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"test_interrupt_at={(InterruptAtSeconds is { } i ? i.ToString("0.#", CultureInfo.InvariantCulture) + " s" : "off")} test_handback_after={(HandBackAfterSeconds is { } h ? h.ToString("0.#", CultureInfo.InvariantCulture) + " s" : "off")} start={(StartAtLogin ? "on" : "off")} control_drill={(ControlDrillDirectory is null ? "off" : "on")}");
}
