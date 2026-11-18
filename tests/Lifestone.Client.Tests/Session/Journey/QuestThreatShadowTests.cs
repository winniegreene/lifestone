using System.Numerics;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Tests.Session.Mage;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 16 E2b, part 2 (docs/automation-engine.md E2 and section 3, "shadow first"): the quest steps' list of attackers beside the threat board (<c>QuestStepLeg.ThreatShadow.cs</c>). Report-only: the steps decide as
/// before; the board is asked for the living creatures of a name heard attacking within the list's 30 s that stand within the walks' 40 m; a difference is one line; the leg's end writes its counts. Over
/// <see cref="QuestRig"/>'s stand-in ACE.
/// </summary>
public sealed class QuestThreatShadowTests
{
    private const uint Npc = 0x7F68200D;
    private const uint Reaver = 0x80001950;
    private const string ReaverName = "Jungle Reaver";

    private static QuestPlace Room => new(QuestRig.HomeCell, new Vector3(89, 12, 94), "her room");

    private static QuestThing Brighteyes => new(22642, "Brighteyes, the Tailor");

    /// <summary>A go to her room on whose first walk a Jungle Reaver <paramref name="metres"/> east of her bites her; the run ended and disposed.</summary>
    private static QuestRig BittenOnTheWalk(float metres)
    {
        var rig = new QuestRig();
        rig.Npc(Npc, Brighteyes.Name, 22642, QuestRig.HomeCell, new Vector3(90, 12, 94));
        rig.Armed([.. ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id)]);
        rig.CastAt = (_, target) => rig.Kill(target);
        var first = true;
        rig.Travel.RunningSteps = r =>
        {
            if (!first || r.Destination.Name != "her room")
            {
                return 0;
            }

            first = false;
            rig.Attacker(Reaver, ReaverName, metres);
            return 8;
        };
        var quest = QuestRig.Quest("test-go", new QuestDone(null, "never said", null), null, QuestRig.Step(0, QuestStepKind.Go, "to Brighteyes", place: Room, target: Brighteyes), QuestRig.Step(1, QuestStepKind.Talk, "her intro", target: Brighteyes));
        var (run, status) = rig.Run(new QuestRun(rig.Context(), quest, JourneyPlans.FromHome, null, "0-0"));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", rig.R.H.Log.Lines.TakeLast(14)));
        (run as IDisposable)?.Dispose(); // the leg's end, as the outer loop ends a station
        return rig;
    }

    [Fact]
    public void The_steps_list_agrees_with_the_threat_board_over_a_fight_back_and_the_leg_writes_its_counts()
    {
        var rig = BittenOnTheWalk(3f);

        Assert.Single(rig.Lines("walk fight-back 1: no attacker left")); // the steps' own fight-back, as before
        var lane = ThreatBoard.For(rig.Session).Lane(QuestStepLeg.StepsDetector);
        Assert.True(lane.Agree > 0 && lane.Differ == 0, string.Join(" | ", rig.Lines(ThreatShadowLane.Token)));
        var line = Assert.Single(rig.Lines(ThreatShadowLane.Token));
        Assert.EndsWith($"threat-shadow: quest-leg: its asks: threat_shadow_agree={lane.Agree} threat_shadow_differ=0 (agreed/differed by detector: quest-steps {lane.Agree}/0)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bites_attacker_past_the_cast_range_is_none_to_the_steps_and_the_shadow_names_it_within_the_walks_reach()
    {
        var rig = BittenOnTheWalk(30f); // a bite is kept to the 15 m cast range by the steps; the board is asked within the walks' 40 m

        Assert.Empty(rig.Lines("walk fight-back")); // the steps decide as before: no attacker, the walk goes on
        var lines = rig.Lines(ThreatShadowLane.Token);
        Assert.Contains("threat-shadow: quest-steps: none (its watch hears since ", lines[0], StringComparison.Ordinal);
        Assert.Contains("the board says Jungle Reaver 0x80001950 at ", lines[0], StringComparison.Ordinal);
        Assert.Contains(" m (1 within 40 m), its name heard attacking ", lines[0], StringComparison.Ordinal);
        var lane = ThreatBoard.For(rig.Session).Lane(QuestStepLeg.StepsDetector);
        Assert.True(lane.Differ > 0, lines[0]);
        Assert.Contains($"threat-shadow: quest-leg: its asks: threat_shadow_agree={lane.Agree} threat_shadow_differ={lane.Differ} ", lines[^1], StringComparison.Ordinal);
    }
}
