using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// The in-game shell's layout files on the Godot user data (M5 6 u1, D33; <see cref="IScreenLayoutStore"/>): <c>user://UI-&lt;world&gt;-&lt;character&gt;-&lt;width&gt;-&lt;height&gt;.txt</c>, the files retail keeps
/// beside the client's preferences (<c>gmGamePlayUI::CreateScreenLayoutPath</c>, 11.6096 0x004EB320). They live under <c>user://</c>, so a run with isolated user data (<c>-IsolateUserData</c>) keeps its
/// own and the owner's are never touched. A name with a path separator is refused (the names are built by <see cref="ScreenLayoutFile.Name"/>, which cannot make one).
/// </summary>
public sealed class GodotScreenLayoutStore : IScreenLayoutStore
{
    private const string Folder = "user://";

    /// <inheritdoc/>
    public string? Read(string name)
    {
        var path = PathOf(name);
        if (!Godot.FileAccess.FileExists(path))
        {
            return null;
        }

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read) ?? throw new IOException($"{path} could not be opened: {Godot.FileAccess.GetOpenError()}");
        return file.GetAsText();
    }

    /// <inheritdoc/>
    public void Write(string name, string text)
    {
        var path = PathOf(name);
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write) ?? throw new IOException($"{path} could not be written: {Godot.FileAccess.GetOpenError()}");
        file.StoreString(text);
    }

    private static string PathOf(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Contains('/', StringComparison.Ordinal) || name.Contains((char)92, StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal))
        {
            throw new IOException($"the layout file name {name} holds a path separator");
        }

        return Folder + name;
    }
}
