using Godot;
using Lifestone.Game.Core;
using Lifestone.Client.Options;
using Lifestone.Client.RetailUi.Panels;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// The video-only options (<c>OptionCatalog.Video</c>): <c>--video-camera=D,H</c> and <c>--video-hide-floaties</c>. Off by default; nothing changes without them. Read from the game's own arguments.
/// </summary>
public static class VideoOptions
{
    /// <summary>The widened chase camera's offset (metres behind, metres up), or null when <c>--video-camera</c> is not given or is bad (a bad value is a warning).</summary>
    public static (float Distance, float Height)? Camera()
    {
        foreach (var argument in CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()))
        {
            if (argument.Name != "--video-camera")
            {
                continue;
            }

            var parts = (argument.Value ?? string.Empty).Split(',');
            if (parts.Length == 2
                && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var distance)
                && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var height)
                && distance is >= 1f and <= 12f && height is >= -1f and <= 6f)
            {
                return (distance, height);
            }

            Log.Warn("video", "--video-camera needs D,H: D from 1 to 12 and H from -1 to 6 metres; the camera is unchanged", ("value", argument.Value));
        }

        return null;
    }

    /// <summary>Sets the panels' video flags from <c>--video-hide-floaties</c> (once, at start-up).</summary>
    public static void ApplyHideFloaties()
    {
        if (!CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()).Any(a => a.Name == "--video-hide-floaties"))
        {
            return;
        }

        GameSpellcastingPanel.HideForVideo = true;
        GameVitaePage.HideForVideo = true;
        Log.Info("video", "--video-hide-floaties: the spellcasting window and the vitae page do not open by themselves");
    }
}
