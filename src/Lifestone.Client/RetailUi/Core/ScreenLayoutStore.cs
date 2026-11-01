using System.Globalization;
using System.Text;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// Where the in-game shell keeps its window layout between logins (M5 6 u1, D33; docs/retail-ui.md section 21.8): retail's client file <c>UI-&lt;world&gt;-&lt;character&gt;-&lt;width&gt;-&lt;height&gt;.txt</c>
/// (<c>gmGamePlayUI::CreateScreenLayoutPath</c>, 11.6096 0x004EB320), beside the client's own preferences. The host decides where the files live (the Godot host writes them under the user data, so a
/// run with isolated user data keeps its own); tests use <see cref="MemoryScreenLayoutStore"/>.
/// </summary>
public interface IScreenLayoutStore
{
    /// <summary>The text of the file <paramref name="name"/>, or null when there is none (retail's <c>LoadScreenLayout</c> returns false for a file it cannot open).</summary>
    string? Read(string name);

    /// <summary>Writes (replaces) the file <paramref name="name"/> with <paramref name="text"/>.</summary>
    void Write(string name, string text);
}

/// <summary>A store in memory (the tests'; a host that wants nothing kept on disk).</summary>
public sealed class MemoryScreenLayoutStore : IScreenLayoutStore
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    /// <summary>The names written, in order of first write.</summary>
    public IReadOnlyCollection<string> Names => _files.Keys;

    /// <summary>How many times <see cref="Write"/> ran.</summary>
    public int WriteCount { get; private set; }

    /// <inheritdoc/>
    public string? Read(string name) => _files.TryGetValue(name, out var text) ? text : null;

    /// <inheritdoc/>
    public void Write(string name, string text)
    {
        _files[name] = text;
        WriteCount++;
    }
}

/// <summary>One window of the layout file: retail's tag, the element it names, and its place and size.</summary>
/// <param name="Tag">Retail's tag, with the angle brackets (<c>&lt;CHAT&gt;</c>).</param>
/// <param name="ElementId">The window's element id in <c>classic_gameplay</c> (the id <c>LoadScreenLayout</c> looks up with <c>GetChildRecursive</c>).</param>
public readonly record struct ScreenLayoutWindow(string Tag, uint ElementId);

/// <summary>
/// The layout file's name and text (retail's <c>gmGamePlayUI::CreateScreenLayoutPath</c> 0x004EB320, <c>SaveScreenLayout</c> 0x004EB9E0, <c>LoadScreenLayout</c> 0x004EB580). One line per window,
/// <c>&lt;TAG&gt; X:%d Y: %d W: %d H: %d</c> (retail reads it with the same pattern, so a line that does not match is skipped). Lifestone adds lines retail does not know, each marked
/// <c>LS-</c> at the start (retail's reader skips them): <c>LS-CHATMASK &lt;TAG&gt; 0x&lt;hex&gt;</c> is a chat window's text-type mask. Window locks (<c>TODO(g6-window-locks)</c>) have no lines: Lifestone has no lock.
/// </summary>
public static class ScreenLayoutFile
{
    /// <summary>The windows <c>LoadScreenLayout</c> knows, in its order, except <c>&lt;SBOX&gt;</c> (see <see cref="SmartBoxTag"/>).</summary>
    public static IReadOnlyList<ScreenLayoutWindow> Windows { get; } =
    [
        new("<CHAT>", 0x10000601), new("<FCH1>", 0x10000505), new("<FCH2>", 0x1000050E), new("<FCH3>", 0x1000050F), new("<FCH4>", 0x10000510),
        new("<EXAM>", 0x100005F7), new("<VITS>", 0x100005FA), new("<SVIT>", 0x100006D5), new("<ENVP>", 0x100005FD), new("<PANS>", 0x100005FF),
        new("<TBAR>", 0x10000603), new("<INDI>", 0x10000611), new("<PBAR>", 0x10000613), new("<COMB>", 0x100006B5), new("<RADA>", 0x100006D2),
    ];

    /// <summary>
    /// Retail's <c>&lt;SBOX&gt;</c> (the SmartBox, 0x1000049A) is saved and loaded by retail. Lifestone's SmartBox is the whole window and the world's view follows it (D40), so a file's line for it is
    /// read and ignored, and none is written (<c>TODO(g6-layout-smartbox)</c>).
    /// </summary>
    public const string SmartBoxTag = "<SBOX>";

    /// <summary>The marker of Lifestone's own chat-mask lines.</summary>
    public const string ChatMaskLine = "LS-CHATMASK";

    /// <summary>
    /// <c>CreateScreenLayoutPath</c> for the special name "#auto": <c>UI-&lt;world&gt;-&lt;character&gt;-&lt;width&gt;-&lt;height&gt;.txt</c>, the current character at the current game-window size. A character
    /// or world name with a character a file name cannot hold gets an underscore in its place.
    /// </summary>
    public static string Name(string world, string character, int width, int height) =>
        string.Create(CultureInfo.InvariantCulture, $"UI-{Clean(world)}-{Clean(character)}-{width}-{height}.txt");

    /// <summary>One window's line, retail's format.</summary>
    public static string Line(string tag, int x, int y, int width, int height) =>
        string.Create(CultureInfo.InvariantCulture, $"{tag} X:{x} Y: {y} W: {width} H: {height}");

    /// <summary>One chat window's mask line.</summary>
    public static string MaskLine(string tag, ulong mask) =>
        string.Create(CultureInfo.InvariantCulture, $"{ChatMaskLine} {tag} 0x{mask:X16}");

    /// <summary>
    /// Reads the text: the windows' places, the chat masks by tag, and how many lines were not understood (a blank line is not counted; a tag nothing in <see cref="Windows"/> names is skipped, as retail
    /// skips it, and is not counted either).
    /// </summary>
    public static ParsedLayout Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var windows = new Dictionary<string, (int X, int Y, int W, int H)>(StringComparer.Ordinal);
        var masks = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var bad = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(ChatMaskLine, StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && parts[2].StartsWith("0x", StringComparison.Ordinal)
                    && ulong.TryParse(parts[2].AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var mask))
                {
                    masks[parts[1]] = mask;
                }
                else
                {
                    bad++;
                }

                continue;
            }

            if (TryParseWindow(line, out var tag, out var rect))
            {
                windows[tag] = rect;
            }
            else
            {
                bad++;
            }
        }

        return new ParsedLayout(windows, masks, bad);
    }

    // sscanf("%s X:%d Y: %d W: %d H: %d"): a token, then the four numbers after their letters.
    private static bool TryParseWindow(string line, out string tag, out (int X, int Y, int W, int H) rect)
    {
        tag = string.Empty;
        rect = default;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries); // "<TAG> X:12 Y: 34 W: 56 H: 78" is eight tokens
        if (parts.Length != 8 || !parts[1].StartsWith("X:", StringComparison.Ordinal) || parts[2] != "Y:" || parts[4] != "W:" || parts[6] != "H:"
            || !int.TryParse(parts[1].AsSpan(2), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[5], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var w)
            || !int.TryParse(parts[7], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var h))
        {
            return false;
        }

        tag = parts[0];
        rect = (x, y, w, h);
        return tag.Length > 2 && tag[0] == '<' && tag[^1] == '>';
    }

    private static string Clean(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            builder.Append(c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c) ? '_' : c);
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }
}

/// <summary>A layout file read by <see cref="ScreenLayoutFile.Parse"/>.</summary>
/// <param name="Windows">The windows' places by tag.</param>
/// <param name="ChatMasks">The chat windows' masks by tag.</param>
/// <param name="BadLines">Lines of the file that were not understood.</param>
public sealed record ParsedLayout(IReadOnlyDictionary<string, (int X, int Y, int W, int H)> Windows, IReadOnlyDictionary<string, ulong> ChatMasks, int BadLines);
