using Lifestone.Client.Tests;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// Not a live test (no backend, no login): the loopback hunt tests read the hunt's verdict line with a regex, and that regex went stale twice (the bow slice added <c>blocked=</c>, the seek slice <c>seeks=</c>; P4 proof run). What this
/// proves: the regex of <see cref="HuntLiveTests"/> matches the line <see cref="HuntScenario.VerdictFields"/> prints today, field for field, and the measures a loopback test builds by name carry <c>blocked</c> and <c>seeks</c> into the game's verdict line.
/// </summary>
public sealed class HuntVerdictShapeTests
{
    [Fact]
    public void The_loopback_regex_matches_the_scenarios_own_verdict_line_with_blocked_and_seeks()
    {
        var scenario = new HuntScenario(new HuntOptions(), new RecordingLogSink(), _ => 40f);
        var line = scenario.VerdictFields();

        Assert.Contains(" blocked=0 seeks=0 ", line, StringComparison.Ordinal);
        Assert.Matches(HuntLiveTests.Verdict, line);
    }

    [Fact]
    public void Measures_built_by_name_pass_blocked_and_seeks_to_the_verdict_line()
    {
        var measures = new HuntLegMeasures(Kills: 1, DamageDealt: 12, DamageTaken: 0, HitsTaken: 0, Evades: 0, XpGained: 30, Fighter: "ok", Swings: 3, Shots: 0, Hits: 2, Misses: 1, Returned: true, Blocked: 4, Seeks: 2);
        var line = HuntLegVerdict.Compose(null, measures).Line;

        Assert.Contains(" blocked=4 seeks=2 ", line, StringComparison.Ordinal);
    }
}
