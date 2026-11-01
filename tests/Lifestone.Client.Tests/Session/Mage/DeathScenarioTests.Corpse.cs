using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The death loop's corpse stage (M4 2.2 s1, D5; <see cref="DeathStages.Corpse"/>) over the same scripted ACE as the rest of this class, now also answering a corpse's open (0196, UseDone, F745s), the pick-ups (0022), the close
/// (0052) and the trip's places: the happy path (found, opened, every item taken one at a time, closed, placed back, <c>corpse_looted=yes items_recovered=n</c>), each precondition's skip, each abort, a death on the trip, and
/// <c>--death-recover-corpse off</c> being M3's run. The re-gear is not scripted here (the rig has no inventory); the kit change it needs is <c>GearScenarioTests</c>'.
/// </summary>
public sealed partial class DeathScenarioTests
{
    private static DeathStages Trip => Core | DeathStages.Corpse;

    private static Rig TripRig(int items = 3)
    {
        var rig = new Rig(buffed: false);
        for (var i = 0; i < items; i++)
        {
            rig.CorpseItems.Add(LootRulesTests.Item(0x80, 10, 5, 0x80000B10u + (uint)i, "Thing " + i, 141u + (uint)i));
        }

        return rig;
    }

    /// <summary>Frames until the scenario enters <paramref name="stage"/> (it must still be running when it does).</summary>
    private static void RunUntilStage(Rig rig, DeathScenario scenario, string stage)
    {
        var end = rig.Now + 900;
        while (scenario.Stage != stage && rig.Now < end)
        {
            Assert.Equal(ScenarioStatus.Running, rig.Frame(scenario));
        }

        Assert.Equal(stage, scenario.Stage);
    }

    private static List<string> StageLog(Rig rig) => [.. Lines(rig).Where(l => l.StartsWith("stage ", StringComparison.Ordinal)).Select(l => l[6..])];

    private static readonly string[] M3Stages = ["arriving", "bind-place", "bind-teleporting", "bind-settle", "bind-use", "bind-line", "den-place", "den-teleporting", "den-settle", "unbuff", "death-mode", "death-wait", "respawn", "respawn-vitals"];

    // --- the happy path

    [Fact]
    public void The_tally_remembers_the_deaths_own_corpse_its_cell_and_position_and_the_clock_of_its_F745()
    {
        var rig = TripRig();
        var scenario = rig.Scenario(new FakeWalk(), Core);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        var tally = scenario.Tally;
        Assert.Equal(rig.CorpseGuid, tally.LastCorpseGuid);
        var at = Assert.NotNull(tally.LastCorpsePosition);
        Assert.Equal(DenArrival, at.CellId);
        Assert.Equal(50f, at.Frame.Origin.X);
        Assert.Equal(30f, at.Frame.Origin.Y);
        Assert.True(tally.LastCorpseAt > 0 && tally.LastCorpseAt <= rig.Now);
    }

    // --- the preconditions

    // --- the aborts: each is named, counted as trip_abort=<reason>, and still places back to Holtburg

    private void AssertAbortedAndBack(Rig rig, DeathScenario scenario, string reason)
    {
        Dump(rig, scenario);
        Assert.Null(scenario.Failure);
        Assert.Equal(reason, scenario.TripAbort);
        Assert.Equal(CorpseLoot.No, scenario.CorpseLooted);
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: trip aborted (" + reason + "):", StringComparison.Ordinal));
        Assert.Contains(" trip_abort=" + reason + " ", scenario.VerdictFields() + " ", StringComparison.Ordinal);
        Assert.Contains("poi", rig.Places[^1], StringComparison.Ordinal); // the abort placed back to Holtburg
        Assert.Equal(HoltburgCell, rig.R.H.State.Self.Position?.CellId);
        Assert.Contains(" died=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    // --- M4 2.2 fix: the first live round's findings (R1 the vitals, R2 partial, R3 the drain, R5 the hold)

    [Fact]
    public void M4_2_2_fix_R1_with_no_suicide_a_stamina_and_a_mana_update_of_zero_before_ACEs_restore_leave_vitals_pct_at_the_restore()
    {
        // The live order of round 1: no suicide, the arrival, the drain of 30 evades reaching the client as stamina 0 and mana 0, then ACE's 75% three seconds after the teleport.
        var rig = new Rig(buffed: false) { ZeroesBeforeRestore = true };
        var scenario = rig.Scenario(new FakeWalk(), Core);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Dump(rig, scenario);

        Assert.Equal(0, rig.Suicides);
        var tally = scenario.Tally;
        Assert.Equal(0.0, tally.FirstStaminaAfterArrivalPercent, 3); // the first updates are the zeroes
        Assert.Equal(0.0, tally.FirstManaAfterArrivalPercent, 3);
        Assert.InRange(tally.VitalsAfterArrivalPercent, 60, 90); // DeathLiveTests (g)'s range: the value at the end of the wait
        Assert.InRange(tally.VitalsAfterArrivalPercent, 70, 80);
        Assert.Matches(new Regex(@" vitals_pct=7\d\.\d "), scenario.VerdictFields());
        var line = Assert.Single(Lines(rig), static l => l.StartsWith("respawn: lifestone (", StringComparison.Ordinal));
        Assert.Matches(new Regex(@"vitals 7\d% \(first health=7\d stamina=0 mana=0; at_wait_end health=7\d stamina=7\d mana=7\d\)"), line);
    }

    // --- a death on the trip

}
