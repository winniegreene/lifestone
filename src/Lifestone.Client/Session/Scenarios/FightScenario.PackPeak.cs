using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 7 finish (checkpoint 3 asked for it, report-only): how big each fight's pack was at its peak. Gate A logged a pack's size only at a death, so the fights she survived had no count. Sampled once a second while the fight runs, by the big pack rule's own read of awake: a living creature within
/// <see cref="PackPeakReachMetres"/>, a creature's awareness range (<see cref="HostileReach.CountWithin"/>). It is an upper bound by distance: a creature that cannot see her counts. One line at the fight's end, <c>pack: fight peak:</c>, beside the verdict (the verdict's own fields are pinned by its readers).
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The awake reach, in metres: the ladder's <c>big_pack.reach_m</c> (the awareness range).</summary>
    public const double PackPeakReachMetres = 24;

    private const double PackPeakSampleSeconds = 1;

    private int _packPeak;

    /// <summary>The largest count this fight has sampled.</summary>
    public int PackPeak => _packPeak;

    private int _packPeakKills;
    private double _packPeakAt = double.NaN;
    private double _packPeakSampledAt = double.NegativeInfinity;

    /// <summary>Samples the count at most once a second while the fight runs and keeps the largest. Allocates nothing.</summary>
    private void PackPeakSample(IGameSessionView session, double clock)
    {
        if (double.IsNaN(_fightStart) || _fightStart == 0 || !double.IsNaN(_fightEnd) || clock - _packPeakSampledAt < PackPeakSampleSeconds)
        {
            return;
        }

        _packPeakSampledAt = clock;
        var awake = HostileReach.CountWithin(session, PackPeakReachMetres);
        if (awake > _packPeak)
        {
            (_packPeak, _packPeakAt, _packPeakKills) = (awake, clock - _fightStart, _tally.Kills);
        }
    }

    /// <summary>The pack line, then the verdict line.</summary>
    private void LogVerdict(IGameSessionView session)
    {
        var at = double.IsNaN(_packPeakAt) ? "n/a" : _packPeakAt.ToString("0", CultureInfo.InvariantCulture);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"pack: fight peak: awake={_packPeak} within {PackPeakReachMetres:0} m (an upper bound by distance) at_s={at} kills_then={_packPeakKills} kills={_tally.Kills} hits_taken={_tally.HitsTaken} cell=0x{session.State.Self.Position?.CellId ?? 0:X8} (report-only)"));
        _log.Info(LogCategory, "verdict " + VerdictFields());
    }
}
