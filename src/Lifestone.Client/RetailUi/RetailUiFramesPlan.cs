using System.Globalization;

namespace Lifestone.Client.RetailUi;

/// <summary>
/// The engine-free half of the review-frames run (<c>--retail-ui-frames</c>; m2-plan G2 step U9): the frames' names, the folder they go to, the
/// window size the run asked for, the images known not to resolve and the verdict line. The Godot half (<c>RetailUiFramesRunner</c>) plays the
/// login screens and takes the pictures; <c>tools\smoke.ps1</c>'s <c>retail-ui</c> step reads the verdict.
/// </summary>
public static class RetailUiFramesPlan
{
    /// <summary>The verdict's wording, which the smoke step matches (frozen with the harness, m2-plan section 4.14).</summary>
    public const string VerdictText = "retail ui frames done";

    /// <summary>The frames, in the order they are taken; the file of frame <c>n</c> (counting from 1) is <see cref="FileName"/>.</summary>
    public static IReadOnlyList<string> FrameNames { get; } =
    [
        "datapatch-checking",
        "datapatch-queue",
        "datapatch-patching-half",
        "charmanage-selected",
        "charmanage-pending-selected",
        "dialog-delete-confirm",
        "dialog-please-wait",
        "charmanage-after-delete",
        "dialog-exit-confirm",
        "dialog-credits-not-in-m2",
        "dialog-entering-world",
        "dialog-enter-refused-offline",
        "dialog-character-error",
        "chargen-heritage",
        "chargen-profession",
        "chargen-skills",
        "chargen-appearance-face",
        "chargen-appearance-clothes",
        "chargen-town",
        "chargen-summary",
        "chargen-dialog-name-in-use",
        "charmanage-after-create",
        "chargen-appearance-olthoi",
        "chargen-summary-olthoi",
        "disconnected",
    ];

    /// <summary>
    /// The one image the retail UI layouts name that no dat holds: DID 0, an image entry that clears the element's image (it is not an
    /// image, so nothing is drawn for it; docs/retail-ui-data.md section 7: 624 images, 623 resolve). A run that draws any other image
    /// as the default graphic fails.
    /// </summary>
    public static IReadOnlyList<uint> KnownMissingImages { get; } = [0u];

    /// <summary>The file name of frame <paramref name="number"/> (counting from 1): <c>NN-name.png</c>.</summary>
    public static string FileName(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, FrameNames.Count);
        return string.Create(CultureInfo.InvariantCulture, $"{number:00}-{FrameNames[number - 1]}.png");
    }

    /// <summary>
    /// The folder the frames go to: <paramref name="directory"/> as given, or, when it is <see cref="RetailUiOptions.DefaultFramesDirectory"/>
    /// (no folder was named), its <c>WxH</c> sub-folder for the window size, so each size keeps its own set.
    /// </summary>
    public static string FolderFor(string directory, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        return directory == RetailUiOptions.DefaultFramesDirectory
            ? string.Create(CultureInfo.InvariantCulture, $"{directory}/{width}x{height}")
            : directory;
    }

    /// <summary>
    /// The window size the run asked for: the last <c>--resolution WxH</c> among the process's own arguments (Godot's, before the <c>--</c> that
    /// starts the game's; the engine hides its own arguments from <c>OS.GetCmdlineArgs()</c>, so the host's command line is read), else the
    /// project's own window size. Null when that argument is not a size.
    /// </summary>
    public static (int Width, int Height)? RequestedWindowSize(IReadOnlyList<string> processArgs, int projectWidth, int projectHeight)
    {
        ArgumentNullException.ThrowIfNull(processArgs);
        (int Width, int Height)? requested = (projectWidth, projectHeight);
        for (var i = 0; i < processArgs.Count && processArgs[i] != "--"; i++)
        {
            if (processArgs[i] == "--resolution")
            {
                requested = i + 1 < processArgs.Count ? ParseSize(processArgs[i + 1]) : null;
            }
        }

        return requested;
    }

    /// <summary>
    /// The verdict line, one line (frozen): <c>retail ui frames done size=WxH scale=S captured=N failures=F missing_images=K</c>. Failures are
    /// zero only when every frame was taken and nothing went wrong.
    /// </summary>
    public static string Verdict(int width, int height, int scale, int captured, int failures, int missingImages) =>
        string.Create(CultureInfo.InvariantCulture, $"{VerdictText} size={width}x{height} scale={scale} captured={captured} failures={failures} missing_images={missingImages}");

    private static (int Width, int Height)? ParseSize(string text)
    {
        var parts = text.Split('x');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            && width > 0 && height > 0
                ? (width, height)
                : null;
    }
}
