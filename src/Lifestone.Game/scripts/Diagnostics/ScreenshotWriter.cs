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
