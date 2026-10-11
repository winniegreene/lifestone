namespace Lifestone.Data.Dat;

/// <summary>
/// Iteration (version) numbers of the opened dat files, read from each database's iteration record (0xFFFF0001).
/// End of retail: portal 2072, cell 982, local 994, highres 497. The client reports these to the server during
/// login, and every on-disk cache of derived data must be keyed by them.
/// </summary>
public readonly record struct DatIterations(int Portal, int Cell, int? Local, int? HighRes)
{
    /// <summary>Compact key fragment for cache paths, e.g. <c>p2072-c982-l994-h497</c>.</summary>
    public string CacheKey => $"p{Portal}-c{Cell}-l{Local?.ToString() ?? "x"}-h{HighRes?.ToString() ?? "x"}";
}
