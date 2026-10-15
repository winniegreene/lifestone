using System.Reflection;

namespace Lifestone.Data.Ui;

/// <summary>
/// Lifestone's own layout files (<c>Ui/Layouts/*.json</c>, format <c>lifestone-layout/1</c>, <see cref="LifestoneLayoutReader"/>) as the assembly
/// embeds them. <see cref="UiLayouts"/> reads and registers them on the first lookup of a DID in the Lifestone range
/// (0x7F000000-0x7FFFFFFF), so every host that has the retail layouts has these too, found and resolved exactly like retail's.
/// </summary>
public static class LifestoneLayoutFiles
{
    private const string Prefix = "Lifestone.Data.Ui.Layouts.";

    /// <summary>The embedded files, by file name (for example <c>tools.json</c>), in name order.</summary>
    public static IReadOnlyList<(string Name, string Json)> ReadAll()
    {
        var assembly = typeof(LifestoneLayoutFiles).Assembly;
        var files = new List<(string, string)>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException($"TODO(g7-layouts): embedded layout {resource} cannot be opened.");
            using var reader = new StreamReader(stream);
            files.Add((resource[Prefix.Length..], reader.ReadToEnd()));
        }

        return files;
    }
}
