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

    /// <summary>
    /// <c>--login-check=buff</c> (M3 2.1 s3; docs/session.md section 30): a separate check, never part of <see cref="All"/>. Enters the mage (BSD Mage), re-seats its wand, runs the buff scenario
    /// (the gear, the magic combat mode, the cycle, the forced re-buff pass, non-combat) in the game's session with the game's own cast visuals, logs off and judges what it measured
    /// (<see cref="Lifestone.Client.Session.Scenarios.BuffLegVerdict"/>).
    /// </summary>
    Buff,

    /// <summary>
    /// <c>--login-check=bsd-fight</c> (M3 3.1): a separate check, never part of <see cref="All"/>. Enters the mage (BSD Mage), goes to the Aluvian Black Spawn Den, tops up the buffs and fights the
    /// tuskers on the arrival's floor with Flame Bolt, logs off and judges what it measured (<see cref="Lifestone.Client.Session.Scenarios.FightLegVerdict"/>).
    /// </summary>
    BsdFight,

    /// <summary>
    /// <c>--login-check=bsd-death</c> (M3 3.2): a separate check, never part of <see cref="All"/>. Enters the mage (BSD Mage), binds at Holtburg's lifestone, goes to the Aluvian Black Spawn Den, is unbuffed by a suicide,
    /// dies to the tuskers, respawns at the lifestone, re-gears, logs off and judges what it measured (<see cref="Lifestone.Client.Session.Scenarios.DeathLegVerdict"/>).
    /// </summary>
    BsdDeath,

    /// <summary>
    /// <c>--login-check=hunt</c> (M4 1.2 s4b): a separate check, never part of <see cref="All"/>. Enters the fighter (Holtburg Hunter), binds at Holtburg's lifestone, hunts the creatures of the hunt grounds with the
    /// Mace or, with <c>--hunt-weapon=missile</c>, the Shortbow and Arrows, returns to Holtburg, logs off and judges what it measured (<see cref="Lifestone.Client.Session.Scenarios.HuntLegVerdict"/>).
    /// </summary>
    Hunt,
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

    /// <summary>The default of <c>--play-screenshot-after</c>, in seconds.</summary>
    public const double DefaultScreenshotAfterSeconds = 5;

    /// <summary>The PNG <c>--play-screenshot</c> saves once the player is placed (play mode), or null.</summary>
    public string? ScreenshotPath { get; init; }

    /// <summary>The seconds <c>--play-screenshot</c> waits after the player is placed (<see cref="DefaultScreenshotAfterSeconds"/>).</summary>
    public double ScreenshotAfterSeconds { get; init; } = DefaultScreenshotAfterSeconds;

    /// <summary>True when <c>--play-screenshot-after</c> was given.</summary>
    public bool ScreenshotAfterGiven { get; init; }

    /// <summary>
    /// The PNG the buff leg saves mid-cast (M3 2.1 s3), or the stem of the fight leg's three frames (M3 3.1 fd): <c>--play-screenshot=PATH</c> with <c>--login-check=buff</c> or <c>bsd-fight</c> and no
    /// <c>--play-screenshot-after</c> moves the path here, so play mode's own capture (a fixed wait after the player is placed, then quit) does not fire; null otherwise.
    /// </summary>
    public string? BuffShotPath { get; init; }

    /// <summary>
    /// The folder <c>--play-panel-shots=DIR</c> saves one frame of each registered panel page to (M3 2.2 s0; <see cref="PanelFramesPlan"/>), or null: in a plain <c>--play</c> run, after the arrival settles, each page is
    /// opened in turn, saved at the window's size and closed, and the run quits like <c>--play-screenshot</c>. Relative paths are taken from the repository root.
    /// </summary>
    public string? PanelShotsDirectory { get; init; }

    /// <summary>
    /// The folder <c>--play-look-shots=DIR</c> saves the look capture's frames to (M4 4.1 lane v0, D27; <see cref="LookShotsPlan"/>), or null: in a play run, once the arrival has settled, each held-item kind the character carries
    /// is wielded in turn and framed close in peace, in its stance and after it is stowed, one look line per frame. Relative paths are taken from the repository root.
    /// </summary>
    public string? LookShotsDirectory { get; init; }

    /// <summary>The view <c>--play-look-shots</c> takes its frames from (<c>--play-look-view=close|hunt</c>, M4 polish pass P2/P3): the close view by default.</summary>
    public LookView LookShotsView { get; init; } = LookView.Close;

    /// <summary>True for <c>--retail-picking</c>: retail's picking rules where Lifestone fixes them (<see cref="RayPicking.Pick"/>; docs/avatars.md §18). Default false.</summary>
    public bool RetailPicking { get; init; }

    /// <summary>True for <c>--retail-sphereless-missile</c>: a projectile whose Setup has no spheres never moves, as retail's sphereless branch (docs/avatars.md section 17; <c>AvatarSet.RetailSpherelessMissile</c>). Default false: it flies on its own velocity.</summary>
    public bool RetailSphereless { get; init; }

    /// <summary>True for <c>--retail-cast-any-mode</c>: a cast is sent outside the magic combat mode (<c>GameSession.RetailCastAnyMode</c>, per session). Default false: refused at once on the client.</summary>
    public bool RetailCastAnyMode { get; init; }

    /// <summary>True for <c>--retail-camera-still</c>: retail's fixed per-frame stall step of the chase camera (CameraManager::UpdateCamera, 11.6096 0x00456780). Default false: the step is scaled by the frame time.</summary>
    public bool RetailCameraStill { get; init; }

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

            if (argument.Name == "--play-panel-shots")
            {
                if (argument.Value.Length == 0)
                {
                    errors.Add("--play-panel-shots: needs a folder (--name=VALUE)");
                }
                else
                {
                    options = options with { PanelShotsDirectory = argument.Value };
                }

                continue;
            }

            if (argument.Name == "--play-look-shots")
            {
                if (argument.Value.Length == 0)
                {
                    errors.Add("--play-look-shots: needs a folder (--name=VALUE)");
                }
                else
                {
                    options = options with { LookShotsDirectory = argument.Value };
                }

                continue;
            }

            if (argument.Name == "--play-look-view")
            {
                if (LookShotsPlan.ParseView(argument.Value) is { } view)
                {
                    options = options with { LookShotsView = view };
                }
                else
                {
                    errors.Add("--play-look-view: expected close or hunt (--name=VALUE)");
                }

                continue;
            }

            if (argument.Name == "--play-screenshot")
            {
                if (argument.Value.Length == 0)
                {
                    errors.Add("--play-screenshot: needs a file path (--name=VALUE)");
                }
                else
                {
                    options = options with { ScreenshotPath = argument.Value };
                }

                continue;
            }

            if (argument.Name == "--play-screenshot-after")
            {
                var known = errors.Count;
                var seconds = OptionValues.Double(argument, errors, DefaultScreenshotAfterSeconds);
                if (errors.Count == known && seconds is >= 0 and <= 600)
                {
                    options = options with { ScreenshotAfterSeconds = seconds, ScreenshotAfterGiven = true };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"--play-screenshot-after: '{argument.Value}' is not 0 to 600");
                }

                continue;
            }

            if (argument.Name == "--retail-picking")
            {
                options = options with { RetailPicking = true };
                continue;
            }

            if (argument.Name == "--retail-cast-any-mode")
            {
                options = options with { RetailCastAnyMode = true };
                continue;
            }

            if (argument.Name == "--retail-sphereless-missile")
            {
                options = options with { RetailSphereless = true };
                continue;
            }

            if (argument.Name == "--retail-camera-still")
            {
                options = options with { RetailCameraStill = true };
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
                        ("buff", LoginCheckKind.Buff),
                        ("bsd-fight", LoginCheckKind.BsdFight),
                        ("bsd-death", LoginCheckKind.BsdDeath),
                        ("hunt", LoginCheckKind.Hunt),
                        ("all", LoginCheckKind.All)),
                };
            }
        }

        if (options is { LoginCheck: LoginCheckKind.Buff or LoginCheckKind.BsdFight or LoginCheckKind.BsdDeath or LoginCheckKind.Hunt, ScreenshotPath: { } shot, ScreenshotAfterGiven: false })
        {
            options = options with { ScreenshotPath = null, BuffShotPath = shot }; // the buff leg takes its own frames, mid-cast, and the fight leg its three
        }

        return options with { Errors = errors };
    }
}
