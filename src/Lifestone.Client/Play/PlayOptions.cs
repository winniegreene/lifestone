using Lifestone.Client.Options;

namespace Lifestone.Client.Play;

/// <summary>Which login check <c>--login-check</c> runs (G6 W12).</summary>
public enum LoginCheckKind
{
    /// <summary>No check: <c>--login-check</c> was not given.</summary>
    None,

    /// <summary>
    /// <c>--login-check=enter</c>: the whole client path of a first login, off-screen, on the test account (data check, character list,
    /// select or create, Enter, portal space, the arrival in the Training Academy, LoginComplete once, the unhide, a wait, a log off back
    /// to the character list, quit). <see cref="Lifestone.Client.Session.Scenarios.LoginCheckScenario"/> is its script.
    /// </summary>
    Enter,

    /// <summary>
    /// <c>--login-check=academy-exit</c>: <see cref="Enter"/>'s path, and between the settle and the log off the walk through the Academy to its exit portal,
    /// a Use of it from range (the server walks the player the rest of the way) and the portal's answer, driving the game's own player
    /// (<see cref="Lifestone.Client.Session.Scenarios.AcademyExitScenario"/>). Ends with the character back at the Academy start.
    /// </summary>
    AcademyExit,

    /// <summary>
    /// <c>--login-check=holtburg-laps</c>: <see cref="Enter"/>'s path, and the three laps of the ring around Holtburg's arrival point after a server
    /// teleport there, driving the game's own player (<see cref="Lifestone.Client.Session.Scenarios.HoltburgLapsScenario"/>). Ends with the character
    /// back where it began.
    /// </summary>
    HoltburgLaps,

    /// <summary><c>--login-check=all</c> (and the bare <c>--login-check</c>): <see cref="Enter"/>, then <see cref="AcademyExit"/>'s walk, then <see cref="HoltburgLaps"/>'s, in one login.</summary>
    All,

    /// <summary>
    /// <c>--login-check=throwaway</c> (m2-plan section 6.9; D59, D61, D65): a separate check, never part of <see cref="All"/>. Creates a throwaway character through the retail screens
    /// (with ACE's two name refusals first), uses the tools panel, logs off, deletes, restores and deletes it, purges it from the account with the graduated character and checks the
    /// account's list is as it began (<see cref="Lifestone.Client.Session.Scenarios.ThrowawayLegScenario"/>).
    /// </summary>
    Throwaway,
}

/// <summary>
/// The Play mode option group's values (registered with their help in <c>OptionCatalog.Play</c>), read by the main scene. The play switch
/// itself, <c>--play</c>, is the Session group's; <c>--login-check</c> implies it.
/// </summary>
public sealed record PlayOptions
{
    /// <summary>The default folder for <c>--play-frames</c>, relative to the repository root (gitignored); the run adds the window's <c>&lt;W&gt;x&lt;H&gt;</c>.</summary>
    public const string DefaultFramesDirectory = "docs/screenshots/m2/shell";

    /// <summary>The login check to run, or <see cref="LoginCheckKind.None"/>.</summary>
    public LoginCheckKind LoginCheck { get; init; }

    /// <summary>
    /// The folder <c>--play-checks</c> saves the shell's review frames to (<see cref="ShellFramesPlan"/>), or null when <c>--play-frames</c> was not given. A run that saves frames
    /// is not a timing run: the frame-time judgement is reported, not judged.
    /// </summary>
    public string? PlayFramesDirectory { get; init; }

    /// <summary>True for <c>--retail-picking</c>: retail's picking rules where Lifestone fixes them (<see cref="RayPicking.Pick"/>; docs/avatars.md §18). Default false.</summary>
    public bool RetailPicking { get; init; }

    /// <summary>True for <c>--retail-unhide-flash</c>: retail's one opaque frame after an unhide that follows a re-hide mid-fade (docs/avatars.md section 17). Default false: that frame is not drawn.</summary>
    public bool RetailUnhideFlash { get; init; }

    /// <summary>
    /// True for <c>--all-sizes</c>: the retail UI checks also place the shell at 1920x1080 and 2560x1440. Default false: tests and checks stay at retail's
    /// native 800x600 (owner rule, 2026-10-14; several resolutions are a polish step at the end of the project).
    /// </summary>
    public bool AllSizes { get; init; }

    /// <summary>The problems <see cref="Parse"/> found, one line each; the option keeps its default.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Parses the group from the user arguments (other arguments are ignored here; the registry reports unknown ones).</summary>
    /// <param name="args">The user arguments (after <c>--</c>).</param>
    public static PlayOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return FromArguments(CommandLineArgument.ParseAll(args));
    }

    /// <summary>Parses the group from arguments already split (see <see cref="Parse"/>).</summary>
    /// <param name="args">The user arguments.</param>
    public static PlayOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new PlayOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            if (argument.Name == "--play-frames")
            {
                options = options with { PlayFramesDirectory = argument.Value.Length == 0 ? DefaultFramesDirectory : argument.Value };
                continue;
            }

            if (argument.Name == "--retail-picking")
            {
                options = options with { RetailPicking = true };
                continue;
            }

            if (argument.Name == "--retail-unhide-flash")
            {
                options = options with { RetailUnhideFlash = true };
                continue;
            }

            if (argument.Name == "--all-sizes")
            {
                options = options with { AllSizes = true };
                continue;
            }

            if (argument.Name != "--login-check")
            {
                continue;
            }

            if (!argument.HasValue)
            {
                options = options with { LoginCheck = LoginCheckKind.All }; // the bare form is every check
            }
            else
            {
                options = options with
                {
                    LoginCheck = OptionValues.Choice(
                        argument,
                        errors,
                        options.LoginCheck,
                        ("enter", LoginCheckKind.Enter),
                        ("academy-exit", LoginCheckKind.AcademyExit),
                        ("holtburg-laps", LoginCheckKind.HoltburgLaps),
                        ("throwaway", LoginCheckKind.Throwaway),
                        ("all", LoginCheckKind.All)),
                };
            }
        }

        return options with { Errors = errors };
    }
}
