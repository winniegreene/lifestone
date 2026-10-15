using Lifestone.Data.Dat;

namespace Lifestone.Data.Tests;

/// <summary>
/// A [Fact] that needs portal + cell and the language dat <c>client_local_English.dat</c> (layouts, string tables); it
/// is reported as skipped, not failed, without them.
/// </summary>
public sealed class RequiresLocalDatsFactAttribute : FactAttribute
{
    public RequiresLocalDatsFactAttribute() => Skip = LocalDatsSkip.Reason();
}

/// <summary>The [Theory] counterpart of <see cref="RequiresLocalDatsFactAttribute"/>.</summary>
public sealed class RequiresLocalDatsTheoryAttribute : TheoryAttribute
{
    public RequiresLocalDatsTheoryAttribute() => Skip = LocalDatsSkip.Reason();
}

/// <summary>The skip reason shared by the language-dat attributes; null when every file is there.</summary>
internal static class LocalDatsSkip
{
    public static string? Reason()
    {
        var dir = DatSet.ResolveDirectory();
        if (!DatSet.Exists())
        {
            return $"Retail dats not found in '{dir}' (set {DatSet.DirectoryEnvironmentVariable}).";
        }

        return File.Exists(Path.Combine(dir, DatSet.LocalFileName)) ? null : $"{DatSet.LocalFileName} not found in '{dir}'.";
    }
}
