namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M6 7 H (the live round 1 of <c>TrophyLiveTests</c>): the two ends of a trophy journey that are named before its verdict is read, pure and tested offline (<c>TrophyRunReadTests</c>), so a rig failure (<c>start:not-on-ground</c> after a death, <c>rung-change:not-present</c>) never stands in for what happened.
/// A death on the run: the island is dangerous and a death is a real end, named by the station it ended, never passed over and never read as the quest's failure. A go that failed: its walk's door lines first, since the Refuge's six Sliding Door weenies 0x7F682000-05 stand at the sills and were never tested live.
/// </summary>
public static class TrophyRunRead
{
    /// <summary>The journey's line when a station ended in a death (<c>journey station=rung-change died: the death station follows (corpse 0x8000118D)</c>).</summary>
    public const string DiedMark = " died: the death station follows";

    /// <summary>What a station's line starts its name with.</summary>
    private const string StationField = "journey station=";

    /// <summary>The name of a death on the run, or null.</summary>
    /// <param name="lines">The journey's log lines.</param>
    public static string? DeathOnTheRun(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var died = lines.FirstOrDefault(l => l.Contains(DiedMark, StringComparison.Ordinal));
        if (died is null)
        {
            return null;
        }

        var at = died.IndexOf(StationField, StringComparison.Ordinal);
        var station = at < 0 ? "unknown" : died[(at + StationField.Length)..].Split(' ')[0];
        var how = lines.FirstOrDefault(l => l.Contains(" died on ", StringComparison.Ordinal)) ?? died;
        return $"trophy:died-on-the-run:{station}: a real death, named (the case passes only on a run that lives; on the island see TODO(m6-facility-hub-rooms)): {how.Trim()}";
    }

    /// <summary>The name of a go step that failed, or null: <c>brighteyes-walk:door</c> with its door lines when the walk logged any, else <c>brighteyes-walk:go</c>.</summary>
    /// <param name="lines">The journey's log lines.</param>
    public static string? GoFailure(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var go = lines.FirstOrDefault(l => l.Contains("quest_step: n=1 kind=go", StringComparison.Ordinal) && l.Contains(" ok=no", StringComparison.Ordinal));
        if (go is null)
        {
            return null;
        }

        var doors = lines.Where(IsDoorLine).ToArray();
        return doors.Length > 0
            ? $"brighteyes-walk:door: the go failed and the walk names a door: {string.Join(" | ", doors.Select(d => d.Trim()))} | {go.Trim()}"
            : $"brighteyes-walk:go: {go.Trim()}";
    }

    /// <summary>The first name that holds: a death, then a go that failed; null when neither did.</summary>
    /// <param name="lines">The journey's log lines.</param>
    public static string? Named(IReadOnlyList<string> lines) => DeathOnTheRun(lines) ?? GoFailure(lines);

    /// <summary>A line about one door (the runner's summary fields <c>door_steps=</c>, <c>door_uses=</c> and <c>doors_opened=</c> name none).</summary>
    private static bool IsDoorLine(string line) =>
        line.Contains("door", StringComparison.OrdinalIgnoreCase) && !line.Contains("door_steps=", StringComparison.Ordinal) && !line.Contains("no door", StringComparison.OrdinalIgnoreCase) && !line.Contains("quest_step:", StringComparison.Ordinal);
}
