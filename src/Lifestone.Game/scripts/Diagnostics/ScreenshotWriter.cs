using Godot;
using Lifestone.Client.Diagnostics;
using Lifestone.Game.Core;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// Saves a viewport as PNG for the automated runs: the path is resolved by the shared rule (<see cref="OutputPaths"/>:
/// relative to the repository root, never inside <c>src/</c> or <c>tests/</c>), folders are created, and every failure
/// is logged and returned as false (never thrown).
/// </summary>
public static class ScreenshotWriter
{
    /// <summary>The repository root: two levels above <c>res://</c> (<c>src/Lifestone.Game/../..</c>).</summary>
    public static string RepositoryRoot =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));

    /// <summary>
    /// Makes the repository root the process's working directory, so every runner that resolves a relative output path
    /// against the working directory (not only this class) writes under the repository (gitignored docs/screenshots,
    /// cache), never into <c>src/Lifestone.Game</c>: Godot makes its <c>--path</c> folder the working directory at
    /// start-up. Only in a source checkout (<c>Lifestone.slnx</c> at the root); returns the root, or null when not.
    /// </summary>
    public static string? UseRepositoryRootAsWorkingDirectory()
    {
        var root = RepositoryRoot;
        if (!System.IO.File.Exists(System.IO.Path.Combine(root, "Lifestone.slnx")))
        {
            return null;
        }

        System.IO.Directory.SetCurrentDirectory(root);
        return root;
    }

    /// <summary>
    /// <paramref name="path"/> as an absolute output path by the shared rule, or null (logged under
    /// <paramref name="category"/>) when it is refused.
    /// </summary>
    public static string? Resolve(string path, string category)
    {
        if (OutputPaths.TryResolve(path, RepositoryRoot, out var full, out var problem))
        {
            return full;
        }

        Log.Error(category, "output path refused", ("path", path), ("problem", problem));
        return null;
    }

    /// <summary>
    /// Like <see cref="Save"/>, but only the read-back of the viewport's image happens on the calling (main) thread; the PNG is encoded and written on a worker, so a run that
    /// measures frame times does not count the encoder's 100 ms as a frame. <paramref name="completed"/> runs on that worker with whether the file was written.
    /// Returns false (logged) when the image could not be read back, and then <paramref name="completed"/> is not called.
    /// </summary>
    /// <param name="viewport">The viewport to capture.</param>
    /// <param name="path">Absolute, or relative to the repository root.</param>
    /// <param name="category">Log category of the caller.</param>
    /// <param name="completed">Called on the worker when the file is written or failed.</param>
    /// <param name="fields">Extra fields for the "screenshot saved" line.</param>
    /// <returns>The milliseconds the main thread spent (the read-back), or -1 when the capture failed.</returns>
    public static double SaveInBackground(Viewport viewport, string path, string category, Action<bool> completed, params (string Key, object? Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(completed);
        if (Resolve(path, category) is not { } full)
        {
            return -1;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Image image;
        try
        {
            image = viewport.GetTexture().GetImage();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Log.Error(category, "screenshot failed", ("path", full), ("error", ex.Message));
            return -1;
        }

        if (image is null || image.IsEmpty())
        {
            Log.Error(category, "screenshot failed: the viewport image is empty", ("path", full));
            return -1;
        }

        var readbackMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var size = $"{image.GetWidth()}x{image.GetHeight()}";
        _ = Lifestone.Game.Core.GameServices.Instance.Workers.RunSync(_ =>
        {
            var ok = false;
            try
            {
                if (System.IO.Path.GetDirectoryName(full) is { Length: > 0 } directory)
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                var error = image.SavePng(full);
                if (error == Error.Ok)
                {
                    Log.Info(category, "screenshot saved", [("path", full), ("size", size), ("main_thread_ms", readbackMs), .. fields]);
                    ok = true;
                }
                else
                {
                    Log.Error(category, "screenshot failed", ("path", full), ("error", error));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Log.Error(category, "screenshot failed", ("path", full), ("error", ex.Message));
            }
            finally
            {
                image.Dispose();
                completed(ok);
            }

            return 0;
        });
        return readbackMs;
    }

    /// <summary>Saves <paramref name="viewport"/>'s current image to <paramref name="path"/>; false (logged) on failure.</summary>
    /// <param name="viewport">The viewport to capture.</param>
    /// <param name="path">Absolute, or relative to the repository root.</param>
    /// <param name="category">Log category of the caller.</param>
    /// <param name="fields">Extra fields for the "screenshot saved" line.</param>
    public static bool Save(Viewport viewport, string path, string category, params (string Key, object? Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (Resolve(path, category) is not { } full)
        {
            return false;
        }

        try
        {
            using var image = viewport.GetTexture().GetImage();
            if (image is null || image.IsEmpty())
            {
                Log.Error(category, "screenshot failed: the viewport image is empty", ("path", full));
                return false;
            }

            if (System.IO.Path.GetDirectoryName(full) is { Length: > 0 } directory)
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            var error = image.SavePng(full);
            if (error != Error.Ok)
            {
                Log.Error(category, "screenshot failed", ("path", full), ("error", error));
                return false;
            }

            Log.Info(category, "screenshot saved", [("path", full), ("size", $"{image.GetWidth()}x{image.GetHeight()}"), .. fields]);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error(category, "screenshot failed", ("path", full), ("error", ex.Message));
            return false;
        }
    }
}
