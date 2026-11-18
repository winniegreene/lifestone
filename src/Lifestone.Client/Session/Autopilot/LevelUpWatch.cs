using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6's hero video (the owner, 2026-11-18): when her next level is near, the skills and attributes page (<see cref="AutopilotPanelDirector.AttributesPanel"/>: its header shows the level, the experience and the meter to
/// the next level) is up, so the level-up shows on screen with the clock. "Near" is read live, no hand-typed number: the experience left to the next level (the XpTable's character level list less her total
/// experience) is at most <see cref="KillsAhead"/> of her largest single gain this run (the experience a kill or a turn-in gave). The page stays up until <see cref="HoldAfterSeconds"/> s after the level
/// changes, so the new level shows. While it wants the page, the panel director and the cantrip look wait. Each start and end is one log line.
/// </summary>
public sealed class LevelUpWatch
{
    /// <summary>How many of her largest gains away the next level counts as near.</summary>
    public const int KillsAhead = 2;

    /// <summary>How long the page stays up after the level changes.</summary>
    public const double HoldAfterSeconds = 8.0;

    private readonly Action<string> _log;
    private long _lastTotal = -1;
    private long _largestGain;
    private int _level;
    private double? _leveledAt;
    private bool _near;

    /// <summary>Makes a watch.</summary>
    /// <param name="log">Where the lines go.</param>
    public LevelUpWatch(Action<string> log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>True while the page should be up: the next level is near, or the level changed less than <see cref="HoldAfterSeconds"/> s ago.</summary>
    public bool WantsPage => _near || _leveledAt is not null;

    /// <summary>Her largest single experience gain this run.</summary>
    public long LargestGain => _largestGain;

    /// <summary>One frame.</summary>
    /// <param name="running">True while the autopilot runs; otherwise the watch forgets its state.</param>
    /// <param name="qualities">Her qualities (level, total experience).</param>
    /// <param name="levelXp">The XpTable's character level list (cumulative), or null when not loaded.</param>
    /// <param name="now">The session clock, in seconds.</param>
    public void Tick(bool running, PlayerQualities qualities, IReadOnlyList<ulong>? levelXp, double now)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        if (!running || !qualities.IsKnown || qualities.Level < 1)
        {
            (_lastTotal, _largestGain, _level, _leveledAt, _near) = (-1, 0, 0, null, false);
            return;
        }

        var total = qualities.TotalExperience;
        var level = qualities.Level;
        if (_lastTotal >= 0 && total > _lastTotal)
        {
            _largestGain = Math.Max(_largestGain, total - _lastTotal);
        }

        _lastTotal = total;
        if (_level != 0 && level > _level)
        {
            _leveledAt = now;
            _log(string.Create(CultureInfo.InvariantCulture, $"level-up watch: level {_level} -> {level}; the attributes page stays up {HoldAfterSeconds:0.#} s"));
        }

        _level = level;
        if (_leveledAt is { } at && now - at >= HoldAfterSeconds)
        {
            _leveledAt = null;
            _log(string.Create(CultureInfo.InvariantCulture, $"level-up watch: level {level} shown; the page goes back to the panel director"));
        }

        var left = Left(levelXp, level, total);
        var near = left is { } l && _largestGain > 0 && l <= _largestGain * KillsAhead;
        if (near && !_near)
        {
            _log(string.Create(CultureInfo.InvariantCulture, $"level-up watch: level {level + 1} is {left} experience away, within {KillsAhead} of her largest gain ({_largestGain}); the attributes page opens"));
        }

        _near = near;
    }

    /// <summary>The experience from <paramref name="total"/> to the level after <paramref name="level"/>, or null without a table or at its cap.</summary>
    /// <param name="levelXp">The XpTable's character level list (cumulative).</param>
    /// <param name="level">Her level.</param>
    /// <param name="total">Her total experience.</param>
    public static long? Left(IReadOnlyList<ulong>? levelXp, int level, long total) =>
        levelXp is null || level < 1 || level + 1 >= levelXp.Count ? null : Math.Max(0, (long)levelXp[level + 1] - total);
}
