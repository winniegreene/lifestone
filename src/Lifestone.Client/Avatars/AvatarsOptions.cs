using Lifestone.Client.Options;

namespace Lifestone.Client.Avatars;

/// <summary>Which camera the offline player starts with (<c>--player-camera</c>).</summary>
public enum PlayerCameraMode
{
    /// <summary>The chase camera behind the character (default).</summary>
    Chase,

    /// <summary>First person, from the character's head.</summary>
    First,
}

/// <summary>
/// The Avatars and player option group's values (registered with their help in <c>OptionCatalog.Avatars</c>), as the main scene
/// parses them (<see cref="Parse(IEnumerable{string})"/>).
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>--offline-player</term><description>Explore mode with the player: a stand-in character (M2 G5), spawned at the start
/// location, moved by retail's keys with its chase camera; M1's free camera and keys are unchanged without the option.</description></item>
/// <item><term>--player-camera=chase|first</term><description>The camera it starts with (default chase).</description></item>
/// <item><term>--physics-overlay</term><description>The overlay of the player's physics.</description></item>
/// <item><term>--motion-stepping=retail</term><description>D26: draw the 30 Hz step positions as retail did, not interpolated ones.</description></item>
/// <item><term>--retail-run-rate-cap</term><description>Restore retail's negative run-rate cap (<c>MotionInterpreter.RetailNegativeRunRateCap</c>).</description></item>
/// <item><term>--offline-player-checks</term><description>The walk checks (a check suite, parsed by <c>GameOptions</c> too): the real offline player driven through its
/// intents. Implies <c>--offline-player</c>.</description></item>
/// <item><term>--offline-player-frames[=DIR]</term><description>Four review frames of the offline player (chase camera in Holtburg, first person, inside a building,
/// mid-jump) to DIR, default <see cref="DefaultFramesDirectory"/>. Implies <c>--offline-player</c>.</description></item>
/// <item><term>--server-object-transitions=on|off</term><description>M5 item 5 (D45): server objects run retail's transitions through
/// the server-object physics host (default on); off is the path without collision (D24), a diagnostics switch for before-and-after
/// measures, not a glitch flag.</description></item>
/// </list>
/// </remarks>
public sealed record AvatarsOptions
{
    /// <summary>The default folder for <c>--offline-player-frames</c>, relative to the repository root (gitignored).</summary>
    public const string DefaultFramesDirectory = "docs/screenshots/m2/player";

    /// <summary>Explore mode has the player (<c>--offline-player</c>).</summary>
    public bool OfflinePlayer { get; init; }

    /// <summary>The camera the player starts with (<c>--player-camera</c>).</summary>
    public PlayerCameraMode Camera { get; init; } = PlayerCameraMode.Chase;

    /// <summary>The physics overlay is on (<c>--physics-overlay</c>).</summary>
    public bool PhysicsOverlay { get; init; }

    /// <summary>The character is drawn at the physics step positions, as retail did (<c>--motion-stepping=retail</c>); false interpolates (D26).</summary>
    public bool RetailStepping { get; init; }

    /// <summary>Retail's negative run-rate cap is restored (<c>--retail-run-rate-cap</c>).</summary>
    public bool RetailRunRateCap { get; init; }

    /// <summary>The walk checks run (<c>--offline-player-checks</c>).</summary>
    public bool WalkChecks { get; init; }

    /// <summary>The folder the review frames go to, or null when <c>--offline-player-frames</c> was not given.</summary>
    public string? FramesDirectory { get; init; }

    /// <summary>Server objects run retail's transitions through their physics host (<c>--server-object-transitions</c>, default on).</summary>
    public bool ServerObjectTransitions { get; init; } = true;

    /// <summary>The problems <see cref="Parse(IEnumerable{string})"/> found, one line each; the option keeps its default.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Parses the group's options with the shared tokenizer; other groups' arguments are skipped.</summary>
    public static AvatarsOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new AvatarsOptions();
        var errors = new List<string>();
        foreach (var argument in CommandLineArgument.ParseAll(args))
        {
            switch (argument.Name)
            {
                case "--offline-player":
                    options = options with { OfflinePlayer = true };
                    break;
                case "--player-camera":
                    options = options with
                    {
                        Camera = OptionValues.Choice(argument, errors, options.Camera, ("chase", PlayerCameraMode.Chase), ("first", PlayerCameraMode.First)),
                    };
                    break;
                case "--physics-overlay":
                    options = options with { PhysicsOverlay = true };
                    break;
                case "--motion-stepping":
                    options = options with { RetailStepping = OptionValues.Choice(argument, errors, false, ("retail", true)) };
                    break;
                case "--retail-run-rate-cap":
                    options = options with { RetailRunRateCap = true };
                    break;
                case "--offline-player-checks":
                    options = options with { OfflinePlayer = true, WalkChecks = true };
                    break;
                case "--server-object-transitions":
                    options = options with
                    {
                        ServerObjectTransitions = OptionValues.Choice(argument, errors, options.ServerObjectTransitions, ("on", true), ("off", false)),
                    };
                    break;
                case "--offline-player-frames":
                    options = options with { OfflinePlayer = true, FramesDirectory = argument.Value.Length == 0 ? DefaultFramesDirectory : argument.Value };
                    break;
            }
        }

        return options with { Errors = errors };
    }
}
