using Lifestone.Client.Session.Scenarios.Journey;
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

    /// <summary>
    /// <c>--login-check=journey --journey=mage|melee|archer</c> (M4 P5b J5): a separate check, never part of <see cref="All"/>. Enters the plan's character on the plan's test account and runs the journey
    /// (<see cref="Lifestone.Client.Session.Scenarios.Journey.JourneyScenario"/>: the stations in one login, on foot between them) in the game's session with the player's own body as the runner's walker,
    /// takes the review frames at the stations and judges the journey's verdict lines.
    /// </summary>
    Journey,

    /// <summary>
    /// <c>--login-check=keep</c> (M5 6 k2, D32 and D33): a separate check, never part of <see cref="All"/>, on the tools test account as Lifestone Throwaway. One process, two logins: the first flips one
    /// character option and moves one window and logs off; the second reads both back from the server's description and the applied layout and puts the option back
    /// (<see cref="Lifestone.Client.Session.RetailChecks.KeepAcrossLogins"/>).
    /// </summary>
    Keep,

    /// <summary>
    /// <c>--login-check=autopilot</c> (M5 4 h1, D57): a separate check, never part of <see cref="All"/>. BSD Mage on the test account is entered and <see cref="Lifestone.Client.Session.Autopilot.AutopilotScenario"/> runs in the game's session until its stop rules or a stop request end it
    /// (<c>--autopilot-minutes</c>, <c>--autopilot-level</c>), then the game logs off and quits; the exit code is the done line's ok.
    /// </summary>
    Autopilot,

    /// <summary>
    /// <c>--login-check=climber-start</c> (M6 onscreen start, owner ruling 2026-11-19: the sprint's whole run in one game window and one process): on the climber's account (<c>qwinnie</c>: Qwinnie; <c>test</c>: Winnie), the old climber is deleted
    /// on the character screen and the new one made through character creation's pages to the climber's template (<see cref="Lifestone.Client.Session.Scenarios.ClimberCreateScenario"/> on screen), checked and the old copy purged; then the
    /// climber's first journey (the Academy and the Pathwarden quest) runs in the same session; then, only when the creation read <c>granted=0</c> and the journey <c>cheat_free=yes granted=0 place=0 recovery_place=0</c>
    /// (<see cref="Lifestone.Client.Session.Scenarios.ClimberStartGate"/>), the play host's autopilot is installed and its <c>--autopilot-*</c> options apply (with <c>--autopilot-start</c>, the level up job starts at once).
    /// </summary>
    ClimberStart,
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

    /// <summary>The journey's plan name from <c>--journey=mage|melee|archer</c> (M4 P5b J5), or null when not given; read by <c>--login-check=journey</c> only.</summary>
    public string? Journey { get; init; }

    /// <summary>The journey's extra frames (<c>--journey-shots</c>, J7): off by default. The seam the hero hook turns on without restructuring the runner.</summary>
    public Lifestone.Client.Session.Scenarios.Journey.JourneyShots JourneyShots { get; init; } = Lifestone.Client.Session.Scenarios.Journey.JourneyShots.Off;

    /// <summary>
    /// <c>--journey-film</c> (the M4 hero video): the journey check frames retail's chase camera for each station as <c>--journey-shots</c> does (the hunt's side turn, the fight's and the den
    /// travel's turn) and logs the showpiece events, but saves no frame, so a screen capture of the window records the run at its full frame rate (a 1080p frame's save stalls the game about 0.5 s).
    /// </summary>
    public bool JourneyFilm { get; init; }

    /// <summary>
    /// <c>--climber-start-wait-file=PATH</c> (M6 onscreen start, with <c>--login-check=climber-start</c> only): the climber start holds at the character list until this file exists (the recorder's go: <c>tools\climb.ps1 -OneWindow -Record</c>
    /// writes it once the window's recording has begun), so the video holds the whole creation; null: no hold. Resolved by the output-path rule (relative to the repository).
    /// </summary>
    public string? ClimberStartWaitFile { get; init; }

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

    /// <summary>The default seconds between two <c>[soak]</c> lines (<c>--soak-line-s</c>, M5 item 0 c3).</summary>
    public const double DefaultSoakLineSeconds = 60.0;

    /// <summary>The longest <c>--soak-line-s</c> accepted: a day.</summary>
    public const double MaxSoakLineSeconds = 86400.0;

    /// <summary>
    /// <c>--soak-line-s=S</c>: seconds between two <c>[soak]</c> INFO lines (memory, collections, frames, the object counts, the link, Godot's errors, the log size; docs/session.md, the soak line).
    /// Default 60; 0 = no line; a bad value is reported by name and the default is kept.
    /// </summary>
    public double SoakLineSeconds { get; init; } = DefaultSoakLineSeconds;

    /// <summary>
    /// <c>--log-copies=console|file|all</c> (M5 item 0 c3, <c>m5-log-triple</c>): where the log lines go besides <c>lifestone.log</c>, which has every line in every mode. Default <see cref="Lifestone.Client.Diagnostics.LogCopies.Console"/>.
    /// </summary>
    public Lifestone.Client.Diagnostics.LogCopies LogCopies { get; init; } = Lifestone.Client.Diagnostics.LogCopies.Console;

    /// <summary>The default cap of <c>lifestone.log</c> in MiB (<c>--log-max-mb</c>, M5 item 7).</summary>
    public const int DefaultLogMaxMb = 256;

    /// <summary>The largest <c>--log-max-mb</c> accepted: 100 GiB.</summary>
    public const int MaxLogMaxMb = 102400;

    /// <summary>The default folder of the soak frames (<c>--soak-frame-dir</c>), under the gitignored cache.</summary>
    public const string DefaultSoakFrameDirectory = "cache/soak/frames";

    /// <summary>
    /// <c>--log-max-mb=N</c> (M5 item 7, <c>m5-log-rotation</c>): when <c>lifestone.log</c> passes N MiB it is rolled to <c>lifestone.1.log</c> (replacing an older one) and a new one begins, so the logs
    /// stay under 2N MiB. Default <see cref="DefaultLogMaxMb"/>; 0 = no cap.
    /// </summary>
    public int LogMaxMb { get; init; } = DefaultLogMaxMb;

    /// <summary>The default of <c>--log-keep</c>: only <c>lifestone.1.log</c> (the behaviour before M5 7 F17).</summary>
    public const int DefaultLogKeep = 1;

    /// <summary>The largest <c>--log-keep</c> accepted.</summary>
    public const int MaxLogKeep = 100000;

    /// <summary>
    /// <c>--log-keep=N</c> (M5 7 F17): how many rolled files stay. A roll makes <c>lifestone.1.log</c> of the current file and moves the older ones up (<c>lifestone.2.log</c>, ...; the oldest has the highest number); those past N
    /// are deleted. Default 1 (only <c>lifestone.1.log</c>); 0 = keep every file (the soak).
    /// </summary>
    public int LogKeep { get; init; } = DefaultLogKeep;

    /// <summary>The longest <c>--log-vitals-s</c> accepted: a day.</summary>
    public const double MaxLogVitalsSeconds = 86400.0;

    /// <summary>
    /// <c>--log-vitals-s=S</c> (M5 7 F17): play mode logs one Info line "vitals: health h/H stamina s/S mana m/M" every S seconds once the player's vitals are known. Default 0 = none (the soak passes 10).
    /// </summary>
    public double LogVitalsSeconds { get; init; }

    /// <summary>The default of <c>--log-objects-m</c>, metres.</summary>
    public const double DefaultLogObjectsMetres = 60.0;

    /// <summary>The largest <c>--log-objects-m</c> accepted.</summary>
    public const double MaxLogObjectsMetres = 10000.0;

    /// <summary>
    /// <c>--log-objects-m=N</c> (M5 7 F17): play mode logs an Info line (category objects) for every server object created or deleted within N metres of the player, with its name, guid and distance, at most
    /// <see cref="Lifestone.Client.Diagnostics.AvatarNoticeTally.ObjectLinesPerWindow"/> lines in each <see cref="Lifestone.Client.Diagnostics.AvatarNoticeTally.ObjectWindowSeconds"/> seconds. Default 60; 0 = none.
    /// </summary>
    public double LogObjectsMetres { get; init; } = DefaultLogObjectsMetres;

    /// <summary>
    /// <c>--soak-frame-s=S</c> (M5 item 7): play mode saves a frame of the window every S seconds after entering the world, to <see cref="SoakFrameDirectory"/>. 0 (default) = none; otherwise
    /// <see cref="Lifestone.Client.Diagnostics.SoakFrameSchedule.MinSeconds"/> to <see cref="Lifestone.Client.Diagnostics.SoakFrameSchedule.MaxSeconds"/>.
    /// </summary>
    public double SoakFrameSeconds { get; init; }

    /// <summary><c>--soak-frame-dir=DIR</c>: where the soak frames go (default <see cref="DefaultSoakFrameDirectory"/>; the output-path rule of <see cref="Lifestone.Client.Diagnostics.OutputPaths"/>).</summary>
    public string SoakFrameDirectory { get; init; } = DefaultSoakFrameDirectory;

    /// <summary><c>--doll-review-frames=DIR</c> (M5 item 7, D129): the paper doll's figure and slots frames, saved once the first time the doll shows; null (default) = none.</summary>
    public string? DollReviewFramesDirectory { get; init; }

    /// <summary><c>--examine-review-frames=DIR</c> (M5 item 7, S6, D136): the examination's corpse and window frames, saved once the first time a container window shows a magic item; null (default) = none.</summary>
    public string? ExamineReviewFramesDirectory { get; init; }

    /// <summary><c>--examine-worn-frames=DIR</c> (M5 item 7, D142): the wielded caster's and the worn robe's examination frames, each once its page shows a spell-changed value (or its tries ran out); null (default) = none.</summary>
    public string? ExamineWornFramesDirectory { get; init; }

    /// <summary><c>--examine-capture-creatures=N</c> (M5 item 7, D137, the failed-roll live capture): inspect each new creature the fight aims at, up to N answers in all; null (default) = off.</summary>
    public int? ExamineCaptureCreatures { get; init; }

    /// <summary>The most answers <c>--examine-capture-creatures</c> takes.</summary>
    public const int MaxExamineCaptureCreatures = 1000;

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

            if (argument.Name == "--soak-line-s")
            {
                var known = errors.Count;
                var seconds = OptionValues.Double(argument, errors, DefaultSoakLineSeconds);
                if (errors.Count == known && seconds is >= 0 and <= MaxSoakLineSeconds)
                {
                    options = options with { SoakLineSeconds = seconds };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"--soak-line-s: '{argument.Value}' is not 0 to {MaxSoakLineSeconds:0} (0 = no line)");
                }

                continue;
            }

            if (argument.Name == "--log-max-mb")
            {
                var known = errors.Count;
                var megabytes = OptionValues.Int(argument, errors, DefaultLogMaxMb);
                if (errors.Count == known && megabytes is >= 0 and <= MaxLogMaxMb)
                {
                    options = options with { LogMaxMb = megabytes };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"--log-max-mb: '{argument.Value}' is not 0 to {MaxLogMaxMb} (0 = no cap)");
                }

                continue;
            }

            if (argument.Name == "--log-keep")
            {
                var known = errors.Count;
                var keep = OptionValues.Int(argument, errors, DefaultLogKeep);
                if (errors.Count == known && keep is >= 0 and <= MaxLogKeep)
                {
                    options = options with { LogKeep = keep };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"--log-keep: '{argument.Value}' is not 0 to {MaxLogKeep} (0 = keep every rolled file)");
                }

                continue;
            }

            if (argument.Name == "--log-vitals-s")
            {
                var known = errors.Count;
                var seconds = OptionValues.Double(argument, errors, 0.0);
                if (errors.Count == known && seconds is >= 0 and <= MaxLogVitalsSeconds)
                {
                    options = options with { LogVitalsSeconds = seconds };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"--log-vitals-s: '{argument.Value}' is not 0 to {MaxLogVitalsSeconds:0} (0 = no vitals line)");
                }

                continue;
            }

            if (argument.Name == "--log-objects-m")
            {
                var known = errors.Count;
                var metres = OptionValues.Double(argument, errors, DefaultLogObjectsMetres);
                if (errors.Count == known && metres is >= 0 and <= MaxLogObjectsMetres)
                {
                    options = options with { LogObjectsMetres = metres };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"--log-objects-m: '{argument.Value}' is not 0 to {MaxLogObjectsMetres:0} (0 = no object lines)");
                }

                continue;
            }

            if (argument.Name == "--soak-frame-s")
            {
                var known = errors.Count;
                var seconds = OptionValues.Double(argument, errors, 0.0);
                if (errors.Count == known && (seconds == 0 || seconds is >= Lifestone.Client.Diagnostics.SoakFrameSchedule.MinSeconds and <= Lifestone.Client.Diagnostics.SoakFrameSchedule.MaxSeconds))
                {
                    options = options with { SoakFrameSeconds = seconds };
                }
                else if (errors.Count == known)
                {
                    errors.Add($"--soak-frame-s: '{argument.Value}' is not 0 or {Lifestone.Client.Diagnostics.SoakFrameSchedule.MinSeconds:0} to {Lifestone.Client.Diagnostics.SoakFrameSchedule.MaxSeconds:0} (0 = no frames)");
                }

                continue;
            }

            if (argument.Name == "--examine-capture-creatures")
            {
                if (int.TryParse(argument.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var answers) && answers is >= 1 and <= MaxExamineCaptureCreatures)
                {
                    options = options with { ExamineCaptureCreatures = answers };
                }
                else
                {
                    errors.Add($"--examine-capture-creatures: '{argument.Value}' is not a whole number from 1 to {MaxExamineCaptureCreatures}");
                }

                continue;
            }

            if (argument.Name == "--examine-review-frames")
            {
                if (argument.Value.Length == 0)
                {
                    errors.Add("--examine-review-frames: needs a folder (--name=VALUE)");
                }
                else
                {
                    options = options with { ExamineReviewFramesDirectory = argument.Value };
                }

                continue;
            }

            if (argument.Name == "--examine-worn-frames")
            {
                if (argument.Value.Length == 0)
                {
                    errors.Add("--examine-worn-frames: needs a folder (--name=VALUE)");
                }
                else
                {
                    options = options with { ExamineWornFramesDirectory = argument.Value };
                }

                continue;
            }

            if (argument.Name == "--doll-review-frames")
            {
                if (argument.Value.Length == 0)
                {
                    errors.Add("--doll-review-frames: needs a folder (--name=VALUE)");
                }
                else
                {
                    options = options with { DollReviewFramesDirectory = argument.Value };
                }

                continue;
            }

            if (argument.Name == "--soak-frame-dir")
            {
                if (argument.Value.Length == 0)
                {
                    errors.Add("--soak-frame-dir: needs a folder (--name=VALUE)");
                }
                else
                {
                    options = options with { SoakFrameDirectory = argument.Value };
                }

                continue;
            }

            if (argument.Name == "--log-copies")
            {
                options = options with
                {
                    LogCopies = OptionValues.Choice(
                        argument,
                        errors,
                        options.LogCopies,
                        ("console", Lifestone.Client.Diagnostics.LogCopies.Console),
                        ("file", Lifestone.Client.Diagnostics.LogCopies.File),
                        ("all", Lifestone.Client.Diagnostics.LogCopies.All)),
                };
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
                        ("journey", LoginCheckKind.Journey),
                        ("autopilot", LoginCheckKind.Autopilot),
                        ("climber-start", LoginCheckKind.ClimberStart),
                        ("keep", LoginCheckKind.Keep),
                        ("all", LoginCheckKind.All)),
                };
            }
        }

        options = JourneyChecked(options, args, errors);
        options = AutopilotChecked(options, args, errors);
        options = ClimberStartChecked(options, args, errors);
        options = JourneyShotsChecked(options, args, errors);
        if (options is { LoginCheck: LoginCheckKind.Buff or LoginCheckKind.BsdFight or LoginCheckKind.BsdDeath or LoginCheckKind.Hunt or LoginCheckKind.Journey or LoginCheckKind.Autopilot, ScreenshotPath: { } shot, ScreenshotAfterGiven: false })
        {
            options = options with { ScreenshotPath = null, BuffShotPath = shot }; // the buff leg takes its own frames, mid-cast, and the fight leg its three
        }

        return options with { Errors = errors };
    }

    /// <summary>
    /// <c>--journey-shots</c>, <c>--journey-shot-frames=N</c> and <c>--journey-shot-step=S</c> (M4 P5b J7): the showpiece bursts of the journey check. Without <c>--journey-shots</c> the other two are errors; outside the journey check all three are errors.
    /// </summary>
    private static PlayOptions JourneyShotsChecked(PlayOptions options, IEnumerable<CommandLineArgument> args, List<string> errors)
    {
        var on = false;
        bool? film = null;
        var frames = JourneyShots.DefaultBurstFrames;
        var step = JourneyShots.OnWithDefaults.StepSeconds;
        var given = new List<string>();
        foreach (var argument in args)
        {
            var known = errors.Count;
            switch (argument.Name)
            {
                case "--journey-shots":
                    on = OptionValues.Switch(argument, errors, on);
                    given.Add(argument.Name);
                    break;
                case "--journey-film":
                    film = OptionValues.Switch(argument, errors, film ?? false);
                    break;
                case "--journey-shot-frames":
                    var n = OptionValues.Int(argument, errors, frames);
                    if (errors.Count == known && n is >= 1 and <= JourneyShots.MaxBurstFrames)
                    {
                        frames = n;
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"--journey-shot-frames: '{argument.Value}' is not 1 to {JourneyShots.MaxBurstFrames}");
                    }

                    given.Add(argument.Name);
                    break;
                case "--journey-shot-step":
                    var seconds = OptionValues.Double(argument, errors, step);
                    if (errors.Count == known && seconds is > 0 and <= 5)
                    {
                        step = seconds;
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"--journey-shot-step: '{argument.Value}' is not a number of seconds above 0 and up to 5");
                    }

                    given.Add(argument.Name);
                    break;
            }
        }

        if (film is not null)
        {
            if (options.LoginCheck != LoginCheckKind.Journey)
            {
                errors.Add("--journey-film: only --login-check=journey reads it");
            }
            else if (on || options.ScreenshotPath is not null)
            {
                errors.Add("--journey-film saves no frame, so it cannot go with " + (on ? "--journey-shots" : "--play-screenshot") + " (a saved frame stalls the filmed run)");
            }
            else
            {
                options = options with { JourneyFilm = film.Value };
            }
        }

        if (given.Count == 0)
        {
            return options;
        }

        if (options.LoginCheck != LoginCheckKind.Journey)
        {
            errors.Add("--journey-shots: only --login-check=journey reads " + string.Join(", ", given));
            return options;
        }

        if (!on)
        {
            if (given.Any(g => g != "--journey-shots"))
            {
                errors.Add("--journey-shot-frames and --journey-shot-step need --journey-shots");
            }

            return options;
        }

        return options with { JourneyShots = new JourneyShots(true, BurstFrames: frames, StepSeconds: step) };
    }

    /// <summary>
    /// <c>--login-check=autopilot</c>'s account rule (M5 4 h1): the mage's plan is the autopilot's, so the run is on the mage's test account (no <c>--account</c> is the test account); any other account is an error.
    /// </summary>
    private static PlayOptions AutopilotChecked(PlayOptions options, IEnumerable<CommandLineArgument> args, List<string> errors)
    {
        if (options.LoginCheck != LoginCheckKind.Autopilot)
        {
            return options;
        }

        var plan = Lifestone.Client.Session.Scenarios.Journey.JourneyPlans.Mage;
        foreach (var argument in args)
        {
            if (argument.Name == "--account" && argument.HasValue && !string.IsNullOrWhiteSpace(argument.Value) && !string.Equals(argument.Value, plan.Account, StringComparison.Ordinal))
            {
                errors.Add($"--login-check=autopilot: the autopilot runs {plan.Character} on the account '{plan.Account}' (give --account={plan.Account} or none); '{argument.Value}' is refused");
            }
        }

        return options;
    }

    /// <summary>
    /// <c>--login-check=climber-start</c>'s rules (M6 onscreen start): the run is on a climber's account (<c>qwinnie</c> or <c>test</c>; no <c>--account</c> is the test account) and <c>--climber-start-wait-file</c> is read by it alone.
    /// </summary>
    private static PlayOptions ClimberStartChecked(PlayOptions options, IEnumerable<CommandLineArgument> args, List<string> errors)
    {
        string? account = null;
        string? waitFile = null;
        foreach (var argument in args)
        {
            if (argument.Name == "--account" && argument.HasValue)
            {
                account = argument.Value;
            }
            else if (argument.Name == "--climber-start-wait-file")
            {
                waitFile = argument.HasValue && !string.IsNullOrWhiteSpace(argument.Value) ? argument.Value : null;
                if (waitFile is null)
                {
                    errors.Add("--climber-start-wait-file: needs a path (relative to the repository)");
                }
            }
        }

        if (options.LoginCheck != LoginCheckKind.ClimberStart)
        {
            if (waitFile is not null)
            {
                errors.Add("--climber-start-wait-file: only --login-check=climber-start reads it");
            }

            return options;
        }

        var effective = string.IsNullOrWhiteSpace(account) ? "test" : account;
        if (Lifestone.Client.Session.Scenarios.Journey.JourneyPlans.Climbers.All(p => !string.Equals(p.Account, effective, StringComparison.Ordinal)))
        {
            errors.Add($"--login-check=climber-start: the climber start runs on a climber's account ({string.Join(" or ", Lifestone.Client.Session.Scenarios.Journey.JourneyPlans.Climbers.Select(p => p.Account))}); '{effective}' is refused");
        }

        return options with { ClimberStartWaitFile = waitFile };
    }

    /// <summary>
    /// <c>--journey=NAME</c> (M4 P5b J5): the plan's name, and the account rule of the headless host: the plan runs on its own test account (mage on <c>test</c>, melee on <c>test-melee</c>, archer on
    /// <c>test-archer</c>); no <c>--account</c> is the test account, so the melee and the archer journeys need theirs. A journey given without the journey check, a bad name and a wrong account are errors.
    /// </summary>
    private static PlayOptions JourneyChecked(PlayOptions options, IEnumerable<CommandLineArgument> args, List<string> errors)
    {
        string? name = null;
        string? account = null;
        foreach (var argument in args)
        {
            if (argument.Name == "--journey")
            {
                name = argument.HasValue ? argument.Value : null;
                if (name is null)
                {
                    errors.Add("--journey: needs a plan (--journey=mage|melee|archer)");
                }
            }
            else if (argument.Name == "--account" && argument.HasValue)
            {
                account = argument.Value;
            }
        }

        if (options.LoginCheck != LoginCheckKind.Journey)
        {
            if (name is not null)
            {
                errors.Add("--journey: only --login-check=journey reads it");
            }

            return options;
        }

        if (name is null)
        {
            errors.Add("--login-check=journey: needs --journey=mage|melee|archer|hero-archer|hero-brawler|hero-mage");
            return options;
        }

        var plan = Lifestone.Client.Session.Scenarios.Journey.JourneyPlans.ForName(name);
        if (plan is null)
        {
            errors.Add($"--journey: '{name}' is not a plan (mage, melee, archer, hero-archer, hero-brawler or hero-mage)");
            return options;
        }

        var effective = string.IsNullOrWhiteSpace(account) ? "test" : account;
        if (!string.Equals(effective, plan.Account, StringComparison.Ordinal))
        {
            errors.Add($"--journey={plan.Name}: the {plan.Name} journey runs {plan.Character} on the account '{plan.Account}' (give --account={plan.Account}); '{effective}' is refused");
            return options;
        }

        return options with { Journey = plan.Name };
    }
}
