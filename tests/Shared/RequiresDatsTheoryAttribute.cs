using Lifestone.Data.Dat;

namespace Lifestone.Data.Tests;

/// <summary>
/// The [Theory] counterpart of <see cref="RequiresDatsFactAttribute"/>: needs the user's retail dats (portal + cell)
/// and is reported as skipped, not failed, when they are absent. Shared by every track (it replaces the per-folder
/// copies wave 1 added under Cells/, Models/, Textures/ and Sky/).
/// </summary>
public sealed class RequiresDatsTheoryAttribute : TheoryAttribute
{
    public RequiresDatsTheoryAttribute()
    {
        if (!DatSet.Exists())
        {
            Skip = $"Retail dats not found in '{DatSet.ResolveDirectory()}' (set {DatSet.DirectoryEnvironmentVariable}).";
        }
    }
}
