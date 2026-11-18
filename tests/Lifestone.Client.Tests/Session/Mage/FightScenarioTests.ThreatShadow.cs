using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 16 E2b, part 2 (docs/automation-engine.md E2 and section 3, "shadow first"): the fight's four detectors beside the threat board (<c>FightScenario.ThreatShadow.cs</c>): the self-defence list, the buff refresh's
/// hostile read, its awake read and the pull rest's read. Report-only: the fight ends as it does without the shadow, and its disposal writes its counts on a line of its own. Over the scripted ACE and walk of the other
/// fight tests.
/// </summary>
public sealed partial class FightScenarioTests
{
    private static List<string> ShadowLines(Rig r) => [.. r.Log.Lines.Where(l => l.Contains(ThreatShadowLane.Token, StringComparison.Ordinal))];

    [Fact]
    public void M6_16_E2b_the_self_defence_list_stands_beside_the_threat_board_and_the_fight_writes_its_counts_at_its_end()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, Den, 22, "Virindi"); // 2 m from her, in her own cell
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk { TicksPerLeg = 3 }));

        MeleeHit(r);
        for (var i = 0; i < 400 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead);
        Assert.Equal(VirindiGuid, r.CastTargets[0]);
        Assert.Equal(1, scenario.Defended); // the fight's own self-defence, as before
        for (var i = 0; i < 8; i++)
        {
            r.Frame(scenario);
        }

        var board = ThreatBoard.For(r.View);
        var defence = board.Lane(FightScenario.DefenceDetector);
        Assert.True(defence.Agree > 0, $"agree {defence.Agree} differ {defence.Differ}: {string.Join(" | ", ShadowLines(r))}");
        Assert.DoesNotContain(ShadowLines(r), l => l.Contains(": its asks: ", StringComparison.Ordinal)); // nothing before the end

        scenario.Dispose();

        var end = Assert.Single(ShadowLines(r), l => l.Contains("threat-shadow: fight: its asks: ", StringComparison.Ordinal));
        var (agree, differ) = (0L, 0L);
        foreach (var detector in new[] { FightScenario.DefenceDetector, FightScenario.RefreshDetector, FightScenario.PullRestDetector })
        {
            (agree, differ) = (agree + board.Lane(detector).Agree, differ + board.Lane(detector).Differ);
        }

        Assert.Contains($"threat-shadow: fight: its asks: threat_shadow_agree={agree} threat_shadow_differ={differ} (agreed/differed by detector: fight-defence {defence.Agree}/{defence.Differ}", end, StringComparison.Ordinal);
        Assert.DoesNotContain("threat_shadow", scenario.VerdictFields(), StringComparison.Ordinal); // the verdict line keeps its pinned shape
    }
}
