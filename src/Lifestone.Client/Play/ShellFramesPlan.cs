using System.Globalization;
using Lifestone.Client.RetailUi;

namespace Lifestone.Client.Play;

/// <summary>
/// The engine-free half of the shell's review frames (<c>--play-checks --play-frames</c>; m2-plan G4 slice g7; docs/retail-ui.md section 21.9): the frames' names, the folder
/// they go to and the verdict line. The Godot half (<c>PlayChecks</c>) takes the pictures when the play checks reach each state; <c>tools\smoke.ps1</c>'s
/// <c>shell-frames</c> step reads the verdict.
/// </summary>
public static class ShellFramesPlan
{
    /// <summary>The verdict's wording, which the smoke step matches (frozen with the harness, m2-plan section 4.14; additive fields only).</summary>
    public const string VerdictText = "shell frames done";

    /// <summary>The frames, as <c>NN-name</c>, in the order they are due (the checks take them in the order of their states, which is not always this one).</summary>
    public static IReadOnlyList<string> FrameNames { get; } =
    [
        "01-shell-portal-space", // the SmartBox's viewport shows the tunnel, the shell around it
        "02-shell-arrival", // the shell over the world on arrival: vitals, chat, radar, spew box
        "03-shell-spew-and-chat", // the spew box and the main chat with lines, the vitals after a server update
        "04-shell-toolbar-and-message", // a NotInM2 message over the shell (the toolbar's inventory button)
        "05-shell-selection-and-vivid-indicator", // an object selected: its name, its health, the vivid target indicator
        "06-character-information", // the character information panel
        "07-options-side-by-side-vitals", // the options panel, the character's side-by-side vitals option chosen
        "08-logout-confirmation", // the logout confirmation over the shell
        "09-shell-automation-inspector", // G7 lane B: the automation inspector over the shell, listing the run's own actions
        "10-shell-tools-panel", // G7 lane A: the tools panel over the shell, after Places, Where am I and Add XP
    ];

    /// <summary>The file name of the frame called <paramref name="name"/> (an entry of <see cref="FrameNames"/>).</summary>
    public static string FileName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return name + ".png";
    }

    /// <summary>
    /// The folder the frames go to: <paramref name="directory"/> as given, or, when it is <see cref="PlayOptions.DefaultFramesDirectory"/> (no folder was named), its
    /// <c>WxH</c> sub-folder for the window size, so each size keeps its own set.
    /// </summary>
    public static string FolderFor(string directory, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        return directory == PlayOptions.DefaultFramesDirectory
            ? string.Create(CultureInfo.InvariantCulture, $"{directory}/{width}x{height}")
            : directory;
    }

    /// <summary>
    /// The verdict line, one line (frozen): <c>shell frames done size=WxH scale=S captured=N failures=F frame_time=reported</c>. A frames run is not a timing run (the read-back
    /// of each frame stalls the frame it is taken in), so the 50 ms frame judgement is reported and not judged; <c>failures</c> is zero only when every frame was taken, the
    /// size asked for and not blank.
    /// </summary>
    public static string Verdict(int width, int height, int scale, int captured, int failures) =>
        string.Create(CultureInfo.InvariantCulture, $"{VerdictText} size={width}x{height} scale={scale} captured={captured} failures={failures} frame_time=reported");

    /// <summary>The window size the run asked for (see <see cref="RetailUiFramesPlan.RequestedWindowSize"/>).</summary>
    public static (int Width, int Height)? RequestedWindowSize(IReadOnlyList<string> processArgs, int projectWidth, int projectHeight) =>
        RetailUiFramesPlan.RequestedWindowSize(processArgs, projectWidth, projectHeight);
}
