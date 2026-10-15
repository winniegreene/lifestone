using Lifestone.Data.Dat;

namespace Lifestone.Data.Tests;

/// <summary>
/// A [Fact] that needs the user's retail dats (portal + cell). It is reported as skipped, not failed, when the
/// files are absent (e.g. on CI). Location: LIFESTONE_DAT_DIR, else the checkout's <c>client</c> folder
/// (<see cref="DatSet.ResolveDirectory(string?)"/>).
/// </summary>
public sealed class RequiresDatsFactAttribute : FactAttribute
{
    public RequiresDatsFactAttribute()
    {
        if (!DatSet.Exists())
        {
            Skip = $"Retail dats not found in '{DatSet.ResolveDirectory()}' (set {DatSet.DirectoryEnvironmentVariable}).";
        }
    }
}

/// <summary>Shares one read-only <see cref="DatSet"/> across the tests of a class.</summary>
public sealed class DatSetFixture : IDisposable
{
    private readonly Lazy<DatSet> _dats = new(() => DatSet.Open());

    public DatSet Dats => _dats.Value;

    public void Dispose()
    {
        if (_dats.IsValueCreated)
        {
            _dats.Value.Dispose();
        }
    }
}
