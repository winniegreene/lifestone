using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

// M6 6 C1 (m6-gate-respawn-lifestone-race): the death station's respawn read when the run did not bind (the autopilot's death station enters at the respawn): the lifestone's CreateObject may land after the vitals wait (the gate's
// round 3), and a journey whose bind station bound classifies against the lifestone it used (JourneyFacts.BoundAt; M6 6 FXD1, 6-D5) instead of the object table.
public sealed partial class DeathScenarioTests
{
    private const uint LateLifestone = 0x80000702;

    [Fact]
    public void M6_6_C1_a_lifestone_whose_create_lands_after_the_vitals_wait_is_found_by_the_re_reads_and_the_respawn_holds()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival, lifestone: false); // no lifestone object at Holtburg when the player lands
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death);

        var status = ScenarioStatus.Running;
        var arrival = double.NaN;
        var created = false;
        for (var i = 0; i < 20000 && status == ScenarioStatus.Running; i++)
        {
            status = rig.Frame(scenario);
            if (double.IsNaN(arrival) && scenario.Stage == "respawn-vitals")
            {
                arrival = rig.Now;
            }

            if (!created && !double.IsNaN(arrival) && rig.Now >= arrival + DeathScenario.VitalsSeconds + 2)
            {
                // The lifestone's create comes 2 s after the one read the scenario used to take (7 s after the arrival).
                var at = rig.R.H.State.Self.Position!.Value;
                var basis = ReducerHarness.Create(LateLifestone, ReducerHarness.Stamps(), new AcPosition(at.CellId, at.Frame.Origin + new Vector3(4, 0, 0), Quaternion.Identity), name: "Lifestone");
                rig.R.H.R.Reducer.OnCreateObject(rig.R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = "Lifestone", ObjectDescriptionFlags = WorldObjectTable.LifestoneFlag } }, isUpdate: false);
                created = true;
            }
        }

        Dump(rig, scenario);

        Assert.True(created);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Contains(" respawn=lifestone ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(Lines(rig), l => l.StartsWith("respawn: no lifestone object within 15 m of the arrival in cell 0xA9B40019 yet (any lifestone object: this run did not bind and no tie is known): re-read each frame for at most 10 s", StringComparison.Ordinal));
        var classified = Assert.Single(Lines(rig), l => l.StartsWith("respawn: classified after ", StringComparison.Ordinal));
        Assert.StartsWith("respawn: classified after 7.", classified, StringComparison.Ordinal); // the arrival's 5 s wait, then 2 s of re-reads
        Assert.Contains("(lifestone; ", classified, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_6_C1_with_no_lifestone_ever_the_re_reads_end_after_RespawnClassifySeconds_and_the_respawn_fails_by_name()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival, lifestone: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.Equal(10, DeathScenario.RespawnClassifySeconds);
        Assert.Contains("respawn=elsewhere: the player arrived in cell 0xA9B40019 with no lifestone object within 15 m after 10 s of re-reads", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(Lines(rig), l => l.StartsWith("respawn: classified after 15.", StringComparison.Ordinal) && l.Contains("(elsewhere; ", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_6_FXD1_a_journey_whose_bind_station_bound_classifies_against_the_lifestone_it_used_without_waiting_for_an_object()
    {
        // 6-D5: the bind station records where it bound (JourneyFacts.BoundAt); the death station's respawn is classified against that point, whatever town it is in (no Holtburg comparison), never against the object table.
        var rig = new Rig(buffed: false, startCell: DenArrival, lifestone: false); // no lifestone object will ever come: the recorded bind decides, not the table
        var boundAt = new CellFrame(0xA9B40019, new AcFrame(new Vector3(84, 7.7f, 94), Quaternion.Identity));
        var facts = new JourneyFacts { Bound = true, BoundAt = boundAt };
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death, new DeathOptions { Facts = facts });

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.False(scenario.Bound);
        Assert.Equal(boundAt, scenario.BoundLifestone);
        Assert.DoesNotContain(Lines(rig), l => l.Contains("re-read each frame", StringComparison.Ordinal)); // no wait on the object table
        Assert.Contains(Lines(rig), l => l.Contains("classified against the lifestone the journey's bind station used in cell 0xA9B40019 (JourneyFacts.BoundAt", StringComparison.Ordinal));

        // The rig lands the player at (50, 20) in the cell, 36 m from that point: a respawn elsewhere, judged at once against the recorded bind.
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("more than 15 m from the lifestone the journey's bind station used (cell 0xA9B40019)", scenario.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain("Holtburg", scenario.Failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)] // a Lifestone Tie alone: it sets Lifestone Recall's target, not where death sends one (ACE WorldObject_Magic.cs 966-975 @95ef426d)
    [InlineData(null, true)] // a bind with no recorded point: named, and the object rule classifies
    public void M6_6_FXD1_a_tie_or_a_bind_with_no_recorded_point_is_no_respawn_point_and_the_object_rule_classifies(bool? tied, bool bound)
    {
        var rig = new Rig(buffed: false, startCell: DenArrival, lifestone: false);
        var facts = new JourneyFacts { LifestoneTied = tied, Bound = bound };
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Den | DeathStages.Death, new DeathOptions { Facts = facts });

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));

        Assert.Null(scenario.BoundLifestone);
        Assert.Contains(Lines(rig), l => l.StartsWith("respawn: no lifestone object within 15 m of the arrival in cell 0xA9B40019 yet (any lifestone object: this run did not bind and no tie is known): re-read each frame", StringComparison.Ordinal));
        Assert.Contains("with no lifestone object within 15 m after 10 s of re-reads", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(bound ? 1 : 0, Lines(rig).Count(l => l.StartsWith("respawn: the journey's facts say a bind ran but hold no point for it", StringComparison.Ordinal)));
    }
}
