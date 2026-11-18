using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Contracts;
using Lifestone.Client.Tests.Session.Mage;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 16 E2b (the one world picture; docs/automation-engine.md E2): the quest steps' pick of the attacker (<c>QuestStepLeg.NearestAttacker</c>, behind <c>LiveAttacker</c>) and the kill's range test
/// (<c>QuestStepLeg.Distance</c>) take a creature where the session draws it (<see cref="IGameSessionView.Live"/>), not where the server last reported it, as the walk's stop beside them (<c>AttackerStopsWalk</c>) already did.
/// Each test states the one state that shows the change: an attacker whose drawn body and reported place differ. The answer by the report alone (what the two read before) is the same rig with no bodies drawn. Over
/// <see cref="QuestRig"/>'s stand-in ACE, at Holtburg (land cells: a body answers within 100 m of the report).
/// </summary>
public sealed class QuestLivePositionTests
{
    private const uint Npc = 0x7F68200D;
    private const uint Reaver = 0x80001950;
    private const string ReaverName = "Jungle Reaver";

    private static QuestPlace Room => new(QuestRig.HomeCell, new Vector3(89, 12, 94), "her room");

    private static QuestThing Brighteyes => new(22642, "Brighteyes, the Tailor");

    private static QuestRig Rig()
    {
        var rig = new QuestRig();
        rig.Npc(Npc, Brighteyes.Name, 22642, QuestRig.HomeCell, new Vector3(90, 12, 94));
        rig.Armed([.. ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id)]);
        rig.CastAt = (_, target) => rig.Kill(target);
        return rig;
    }

    private static QuestRun GoRun(QuestRig rig) =>
        new(rig.Context(), QuestRig.Quest("test-go", new QuestDone(null, "never said", null), null, QuestRig.Step(0, QuestStepKind.Go, "to Brighteyes", place: Room, target: Brighteyes), QuestRig.Step(1, QuestStepKind.Talk, "her intro", target: Brighteyes)), JourneyPlans.FromHome, null, "0-0");

    /// <summary>
    /// The first walk to her room is attacked by a creature the server last reported <paramref name="reportMetres"/> east of her; with <paramref name="bodyMetres"/> given, the session draws bodies and the creature's
    /// stands that far east of her. The walk takes 8 steps when nothing stops it.
    /// </summary>
    private static QuestRig AttackedOnTheWalk(float reportMetres, float? bodyMetres)
    {
        var rig = Rig();
        var first = true;
        rig.Travel.RunningSteps = r =>
        {
            if (!first || r.Destination.Name != "her room")
            {
                return 0;
            }

            first = false;
            rig.Attacker(Reaver, ReaverName, reportMetres); // a bite: the notice names it, and the steps' list keeps it to the cast range (15 m)
            if (bodyMetres is { } metres)
            {
                rig.Draw(new ScriptedBodies().StandEast(Reaver, rig.R.H.State.Self.Position!.Value, metres));
            }

            return 8;
        };
        var (_, status) = rig.Run(GoRun(rig));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", rig.R.H.Log.Lines.TakeLast(14)));
        return rig;
    }

    private static int CastsAtIt(QuestRig rig) => rig.Events.Count(e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith("@80001950", StringComparison.Ordinal));

    [Fact]
    public void An_attacker_drawn_3_m_from_her_and_last_reported_30_m_back_stops_the_walk_and_is_cast_at_from_where_she_stands()
    {
        // before (the report alone): 30 m is past the 15 m a bite is kept to, so the steps had no attacker: the walk went on and nothing was cast while it bit her
        var before = AttackedOnTheWalk(reportMetres: 30, bodyMetres: null);
        Assert.Single(before.Lines("attacked by Jungle Reaver"));
        Assert.Empty(before.Lines("walk fight-back"));
        Assert.Equal(0, CastsAtIt(before));

        // after: its body stands 3 m from her: the walk stops, the bolt is cast from where she stands (no walk toward the place the report left it), and the same walk goes on
        var after = AttackedOnTheWalk(reportMetres: 30, bodyMetres: 3);
        Assert.Single(after.Lines("the walk is stopped: 'Jungle Reaver' 0x80001950 attacked the player and stands 3.0 m away (within 40 m): fight back (M5 D88)"));
        Assert.Single(after.Lines("fight-back: target 'Jungle Reaver' 0x80001950 at 3.0 m")); // M6 16 Q8: the skirmish's line
        Assert.Equal(1, CastsAtIt(after));
        Assert.Empty(after.Lines("reach=walk"));
        Assert.DoesNotContain(after.Travel.Requests, r => r.Destination.Name == ReaverName);
        Assert.Single(after.Lines("walk fight-back 1: no attacker left"));
        Assert.Single(after.Lines("walk fight-back: check after: the walk to to Brighteyes went on after 1 fight-back(s): it arrived"));
    }

    [Fact]
    public void An_attacker_last_reported_3_m_from_her_and_drawn_30_m_off_is_no_longer_the_steps_attacker()
    {
        // before (the report alone): 3 m, inside the 15 m: the walk stopped and she cast at it
        var before = AttackedOnTheWalk(reportMetres: 3, bodyMetres: null);
        Assert.Single(before.Lines("walk fight-back 1: check before"));
        Assert.Equal(1, CastsAtIt(before));

        // after: its body stands 30 m off, past the 15 m a bite is kept to: the steps have no attacker, the walk goes on, nothing is cast
        var after = AttackedOnTheWalk(reportMetres: 3, bodyMetres: 30);
        Assert.Single(after.Lines("attacked by Jungle Reaver"));
        Assert.Empty(after.Lines("walk fight-back"));
        Assert.Equal(0, CastsAtIt(after));
    }
}
