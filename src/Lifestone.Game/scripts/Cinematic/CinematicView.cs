using Godot;
using Lifestone.Client.Cinematic;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.Cinematic;

/// <summary>
/// What both cinematic directors share: loading the shot list, the capture settings of the viewport (anti-aliasing for
/// this run only, never the project's), the black cover over frames that must not reach the film, and the log lines
/// tools\hero-video.ps1 reads.
/// </summary>
public static class CinematicView
{
    /// <summary>Canvas layer of the black cover: above every HUD.</summary>
    public const int CoverLayer = 120;

    /// <summary>
    /// True when frames are stepped at a fixed rate (Godot's <c>--write-movie</c> or <c>--fixed-fps</c>): shot time then
    /// counts frames, so the film is exactly reproducible; otherwise it follows the real frame time (a preview).
    /// </summary>
    public static bool FixedStep { get; } = OS.HasFeature("movie") || OS.GetCmdlineArgs().Any(a => a is "--write-movie" or "--fixed-fps");

    /// <summary>True under Godot's Movie Maker (the engine's <c>movie</c> feature tag).</summary>
    public static bool Recording { get; } = OS.HasFeature("movie") || OS.GetCmdlineArgs().Any(a => a == "--write-movie");

    /// <summary>
    /// Reads and parses the shot list (a relative path is taken from the repository root). Returns null (logged under
    /// <paramref name="category"/>) when it cannot be read or does not parse.
    /// </summary>
    public static ShotList? Load(string path, string category)
    {
        var full = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(ScreenshotWriter.RepositoryRoot, path);
        string text;
        try
        {
            text = System.IO.File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Error(category, "shot list not readable", ("path", full), ("error", ex.Message));
            return null;
        }

        var errors = new List<string>();
        var list = ShotList.Parse(text, errors);
        foreach (var error in errors)
        {
            Log.Error(category, "shot list problem", ("path", full), ("problem", error));
        }

        if (list is not null)
        {
            Log.Info(category, "shot list", ("path", full), ("title", list.Title), ("shots", list.Shots.Count), ("fps", list.Fps), ("size", $"{list.Width}x{list.Height}"),
                ("msaa", list.Msaa), ("screen_space_aa", list.ScreenSpaceAA), ("scale3d", list.Scale3D));
        }

        return list;
    }

    /// <summary>The shots of <paramref name="scene"/> to film: the ids asked for (in list order), or all of that scene.</summary>
    public static IReadOnlyList<Shot> Select(ShotList list, ShotScene scene, IReadOnlyList<string> ids, string category, out bool missing)
    {
        missing = false;
        foreach (var id in ids)
        {
            if (list.Find(id) is null)
            {
                Log.Error(category, "no such shot in the list", ("id", id));
                missing = true;
            }
        }

        return list.Shots
            .Where(s => s.Scene == scene && (ids.Count == 0 || ids.Contains(s.Id, StringComparer.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>Anti-aliasing for this run (the project settings stay as they are).</summary>
    public static void ApplyCaptureSettings(Viewport viewport, ShotList list, string category)
    {
        viewport.Msaa3D = list.Msaa switch
        {
            2 => Viewport.Msaa.Msaa2X,
            4 => Viewport.Msaa.Msaa4X,
            8 => Viewport.Msaa.Msaa8X,
            _ => Viewport.Msaa.Disabled,
        };
        viewport.ScreenSpaceAA = list.ScreenSpaceAA switch
        {
            "fxaa" => Viewport.ScreenSpaceAAEnum.Fxaa,
            "smaa" => Viewport.ScreenSpaceAAEnum.Smaa,
            _ => Viewport.ScreenSpaceAAEnum.Disabled,
        };
        viewport.UseTaa = false; // TAA smears the moving camera and particles
        viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = (float)list.Scale3D;
        var size = viewport.GetVisibleRect().Size;
        Log.Info(category, "capture settings", ("msaa", viewport.Msaa3D), ("screen_space_aa", viewport.ScreenSpaceAA), ("scale3d", viewport.Scaling3DScale),
            ("viewport", $"{size.X:0}x{size.Y:0}"), ("window", DisplayServer.WindowGetSize()), ("screen", DisplayServer.ScreenGetSize()),
            ("screen_scale", DisplayServer.ScreenGetScale()), ("fixed_step", FixedStep), ("movie", Recording));
        if ((int)size.X != list.Width || (int)size.Y != list.Height)
        {
            Log.Warn(category, "the window is not the shot list's size (tools\\godot.ps1 -Resolution)", ("viewport", $"{size.X:0}x{size.Y:0}"), ("wanted", $"{list.Width}x{list.Height}"));
        }
    }

    /// <summary>A full-screen black cover on its own canvas layer (shown while a shot loads, so no such frame reaches the film).</summary>
    public static CanvasLayer AddCover(Node parent)
    {
        var layer = new CanvasLayer { Name = "CinematicCover", Layer = CoverLayer };
        var rect = new ColorRect { Name = "Black", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(rect);
        parent.AddChild(layer);
        return layer;
    }

    /// <summary>
    /// Logs a recorded shot's frames, the line tools\hero-video.ps1 trims by: Movie Maker numbers frames from 0 in the
    /// order the engine draws them, and <paramref name="firstFrame"/> is the index of the shot's first frame.
    /// </summary>
    public static void LogRecorded(string category, Shot shot, long firstFrame, int frames, int fps, double waitedSeconds, int waitedFrames, double recordSeconds)
    {
        Log.Info(
            category,
            "shot recorded",
            ("id", shot.Id),
            ("first_frame", firstFrame),
            ("last_frame", firstFrame + frames - 1),
            ("frames", frames),
            ("fps", fps),
            ("seconds", frames / (double)fps),
            ("waited_seconds", waitedSeconds),
            ("waited_frames", waitedFrames),
            ("record_wall_seconds", recordSeconds),
            ("wall_ms_per_frame", recordSeconds * 1000.0 / Math.Max(1, frames)));
    }
}
