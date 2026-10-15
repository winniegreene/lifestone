using Lifestone.Client.Options;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;

namespace Lifestone.Client.RetailUi;

/// <summary>How the main scene runs the retail UI (<c>--retail-ui</c>).</summary>
public enum RetailUiMode
{
    /// <summary>No retail UI is installed: the main scene is M1's explore mode, unchanged (the default).</summary>
    Off,

    /// <summary>
    /// The retail screens run on stand-ins: with no live session the screens' own logic answers the way a server would
    /// (the offline stand-in responder of <c>StandInSessions</c>), so every screen can be clicked through. The data check, character
    /// management and the disconnected screen are the real screens (docs/retail-ui.md §18); character creation and the game screen are still
    /// stand-ins (the layouts' art with no behaviour, labelled as a placeholder on screen) until G3 and G4.
    /// </summary>
    StandIn,

    /// <summary>Play mode (<c>--play</c>): the screens run on the live session. Set by the application, never parsed from the command line.</summary>
    Live,
}

/// <summary>
/// The Retail UI option group's values (registered with their help in <c>OptionCatalog.RetailUi</c>), as the game's retail UI
/// layer parses them (<see cref="Parse(IEnumerable{string})"/>; docs/retail-ui.md §15).
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>--retail-ui=off|standin</term><description>Install the retail UI in the main scene (default off: M1's explore mode, unchanged).
/// <c>--retail-ui-start</c>, <c>--retail-ui-frames</c> and <c>--retail-ui-only</c> imply <c>standin</c>; giving them with <c>--retail-ui=off</c> is an error.</description></item>
/// <item><term>--ui-scale=N</term><description>The integer UI scale (default: the largest that leaves at least 800x600 UI pixels,
/// <see cref="UiScalePolicy"/>); <c>--ui-scale=1</c> restores retail's pixel-for-pixel UI.</description></item>
/// <item><term>--retail-ui-start=SCREEN</term><description>The first screen (see <see cref="StartNames"/>; default: the data check, as
/// <c>gmUIFlow</c> starts).</description></item>
/// <item><term>--retail-ui-frames[=DIR]</term><description>Play the login screens offline, capture the review frames to DIR (default <see cref="DefaultFramesDirectory"/> plus a
/// <c>&lt;W&gt;x&lt;H&gt;</c> sub-folder) and quit; implies <c>--retail-ui-only</c>'s empty world.</description></item>
/// <item><term>--retail-ui-only</term><description>Run the retail UI alone: no world, no sky.</description></item>
/// <item><term>--retail-ui-glitches</term><description>Restore the retail glitches Lifestone fixes by default
/// (<see cref="UiRetailGlitches.All"/>; docs/retail-ui.md §14), the fidelity rule's flag.</description></item>
/// <item><term>--retail-disconnect</term><description>Retail's exit from the disconnected screen (D43); by default its button reconnects.</description></item>
/// </list>
/// </remarks>
public sealed record RetailUiOptions
{
    /// <summary>The default folder for <c>--retail-ui-frames</c>, relative to the repository root (gitignored); the runner adds the window's <c>&lt;W&gt;x&lt;H&gt;</c>.</summary>
    public const string DefaultFramesDirectory = "docs/screenshots/m2/ui";

    /// <summary>The largest <c>--ui-scale</c>: far above any real window (an 8K display wants 8).</summary>
    public const int MaximumScale = 16;

    /// <summary>The screen names <c>--retail-ui-start</c> takes, with the UI mode each starts (<see cref="UiModes"/>).</summary>
    public static IReadOnlyList<(string Name, uint Mode)> Screens { get; } =
    [
        ("datapatch", UiModes.DataPatch),
        ("intro", UiModes.Intro),
        ("charmanage", UiModes.CharacterManagement),
        ("game", UiModes.GamePlay),
        ("epilogue", UiModes.Epilogue),
        ("disconnected", UiModes.Disconnected),
        ("chargen", UiModes.CharGen),
        ("credits", UiModes.Credits),
    ];

    /// <summary>The names of <see cref="Screens"/>, comma separated, for messages.</summary>
    public static string StartNames => string.Join(", ", Screens.Select(s => s.Name));

    /// <summary>Whether and how the retail UI runs (<c>--retail-ui</c>, or implied by the options that need it).</summary>
    public RetailUiMode Mode { get; init; }

    /// <summary>The UI scale given by <c>--ui-scale</c>, or null for <see cref="UiScalePolicy"/>'s rule.</summary>
    public int? ScaleOverride { get; init; }

    /// <summary>The first screen's UI mode (<c>--retail-ui-start</c>), or null for the flow's own first mode.</summary>
    public uint? StartMode { get; init; }

    /// <summary>The folder the review frames go to, or null when <c>--retail-ui-frames</c> was not given.</summary>
    public string? FramesDirectory { get; init; }

    /// <summary>The retail UI runs alone (<c>--retail-ui-only</c>).</summary>
    public bool UiOnly { get; init; }

    /// <summary>The retail glitches the host restores (<c>--retail-ui-glitches</c>: all of them; none by default).</summary>
    public UiRetailGlitches Glitches { get; init; }

    /// <summary>The disconnected screen's button exits, as retail's (<c>--retail-disconnect</c>, D43).</summary>
    public bool RetailDisconnect { get; init; }

    /// <summary>The problems <see cref="Parse(IEnumerable{string})"/> found, one line each; the option keeps its default.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>True when the main scene installs the retail UI.</summary>
    public bool Enabled => Mode != RetailUiMode.Off;

    /// <summary>
    /// Parses the group's options with the shared tokenizer and value parsers (<see cref="CommandLineArgument"/>,
    /// <see cref="OptionValues"/>): a value that does not parse is added to <see cref="Errors"/> and the option keeps its
    /// default. Other groups' arguments are skipped.
    /// </summary>
    public static RetailUiOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new RetailUiOptions();
        var errors = new List<string>();
        var explicitOff = false;
        string? needsUi = null;
        foreach (var argument in CommandLineArgument.ParseAll(args))
        {
            switch (argument.Name)
            {
                case "--retail-ui":
                    var mode = OptionValues.Choice(argument, errors, options.Mode, ("off", RetailUiMode.Off), ("standin", RetailUiMode.StandIn));
                    explicitOff = argument.Value == "off";
                    options = options with { Mode = mode };
                    break;
                case "--ui-scale":
                    var problems = errors.Count;
                    var scale = OptionValues.Int(argument, errors, 0);
                    if (errors.Count == problems)
                    {
                        if (scale is >= 1 and <= MaximumScale)
                        {
                            options = options with { ScaleOverride = scale };
                        }
                        else
                        {
                            errors.Add($"--ui-scale: {scale} is not a whole number from 1 to {MaximumScale}");
                        }
                    }

                    break;
                case "--retail-ui-start":
                    needsUi ??= argument.Name;
                    var found = Screens.FirstOrDefault(s => string.Equals(s.Name, argument.Value, StringComparison.Ordinal));
                    if (found.Name is null)
                    {
                        errors.Add($"--retail-ui-start: '{argument.Value}' is not one of {StartNames}");
                    }
                    else
                    {
                        options = options with { StartMode = found.Mode };
                    }

                    break;
                case "--retail-ui-frames":
                    needsUi ??= argument.Name;
                    options = options with { FramesDirectory = argument.Value.Length == 0 ? DefaultFramesDirectory : argument.Value };
                    break;
                case "--retail-ui-only":
                    needsUi ??= argument.Name;
                    options = options with { UiOnly = true };
                    break;
                case "--retail-ui-glitches":
                    options = options with { Glitches = UiRetailGlitches.All };
                    break;
                case "--retail-disconnect":
                    options = options with { RetailDisconnect = true };
                    break;
            }
        }

        if (needsUi is not null)
        {
            if (explicitOff)
            {
                errors.Add($"{needsUi} needs the retail UI, but --retail-ui=off was given; the retail UI stays off");
            }
            else if (options.Mode == RetailUiMode.Off)
            {
                options = options with { Mode = RetailUiMode.StandIn };
            }
        }

        return options with { Errors = errors };
    }
}
