using Lifestone.Client.Session.Autopilot;

namespace Lifestone.Headless;

/// <summary>
/// The headless host's <see cref="IAutopilotCheckpointStore"/> (M5 item 4 h3): the checkpoint is a file. <see cref="Write"/> writes a temporary file beside it and moves it over the checkpoint in one step
/// (<c>File.Move(temp, path, overwrite: true)</c>, so a reader sees the old file or the new one, never half of either); <see cref="SetAside"/> renames the file to <c>&lt;file&gt;.aside</c>, then <c>.aside2</c> and so on, and never deletes it.
/// The client does no file I/O (<c>ArchitectureTests</c>), so each host has its own: the game's is <c>UserDataCheckpointStore</c> in <c>Lifestone.Game</c> (the same steps over <c>user://autopilot</c>).
/// </summary>
/// <param name="path">The checkpoint file.</param>
internal sealed class AutopilotFileCheckpointStore(string path) : IAutopilotCheckpointStore
{
    /// <summary>The checkpoint file.</summary>
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <inheritdoc/>
    public string Describe => System.IO.Path.GetFileName(Path);

    /// <inheritdoc/>
    public string? Read() => File.Exists(Path) ? File.ReadAllText(Path) : null;

    /// <inheritdoc/>
    public void Write(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, Path, overwrite: true);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new IOException(e.Message, e); // the autopilot names an IOException and tries again at the next state change
        }
    }

    /// <inheritdoc/>
    public string SetAside()
    {
        var target = Path + ".aside";
        for (var n = 2; File.Exists(target); n++)
        {
            target = Path + ".aside" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        File.Move(Path, target);
        return target;
    }
}
