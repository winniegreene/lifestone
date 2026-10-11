using Lifestone.Data.Dat;

namespace Lifestone.Data.Tests.Textures;

/// <summary>
/// A [Fact] that needs portal + cell and the optional <c>client_highres.dat</c>; skipped (not failed) without them.
/// </summary>
public sealed class RequiresHighResDatsFactAttribute : FactAttribute
{
    public RequiresHighResDatsFactAttribute()
    {
        var dir = DatSet.ResolveDirectory();
        if (!DatSet.Exists())
        {
            Skip = $"Retail dats not found in '{dir}' (set {DatSet.DirectoryEnvironmentVariable}).";
        }
        else if (!File.Exists(Path.Combine(dir, DatSet.HighResFileName)))
        {
            Skip = $"{DatSet.HighResFileName} not found in '{dir}'.";
        }
    }
}

/// <summary>A [Theory] that also needs <c>client_highres.dat</c> (see <see cref="RequiresHighResDatsFactAttribute"/>).</summary>
public sealed class RequiresHighResDatsTheoryAttribute : TheoryAttribute
{
    public RequiresHighResDatsTheoryAttribute()
    {
        var dir = DatSet.ResolveDirectory();
        if (!DatSet.Exists())
        {
            Skip = $"Retail dats not found in '{dir}' (set {DatSet.DirectoryEnvironmentVariable}).";
        }
        else if (!File.Exists(Path.Combine(dir, DatSet.HighResFileName)))
        {
            Skip = $"{DatSet.HighResFileName} not found in '{dir}'.";
        }
    }
}
