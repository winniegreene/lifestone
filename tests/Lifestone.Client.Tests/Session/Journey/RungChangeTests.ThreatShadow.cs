using System.Numerics;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Threat;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 16 E2a (docs/automation-engine.md E2; m6-plan.md D139): the travel leg's part of the threat board's shadow (<c>TravelLegThreatShadow.cs</c>). M6 16 Q2: the leg's attackers present on a walk are read off the board
/// (the leg's own question). M6 16 Q2b: the fight-back's pick and the lull test are read off it too; the leg's lanes and its line of their counts are gone.
/// </summary>
public sealed partial class RungChangeTests
{
    /// <summary>
    /// The run past of a slower attacker (it hits her as the run begins and is left behind). M6 16 Q2: the walk's attackers present are read off the board, with the leg's own question; the leg's watch heard the same attack, so
    /// the run past judges as before. The walk's lane is gone, and with no lull test or pick asked the leg writes no line of the shadow.
    /// </summary>
    [Fact]
    public void A_travel_leg_under_attack_reads_its_attackers_off_the_threat_board_and_runs_past_as_before()
    {
        var (t, context) = RunPastRig(anim: 1.0, seconds: 12);
        t.Travel.OnRun = (_, ran, _) =>
        {
            if (ran == 0)
            {
                t.Attacker(Runner, "Armored Tusker", 3f, wcid: 11540);
            }
        };

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("run past: outrun: 'Armored Tusker' 0x80001960")); // the leg's own rule, as before
        Assert.Empty(t.Lines(ThreatShadowLane.Token));
        var board = ThreatBoard.For(t.Session);
        Assert.Equal((1, 11540u), (board.Find("Armored Tusker")!.Hits, board.WcidOf("Armored Tusker")));
    }

    /// <summary>
    /// M6 16 Q2b: the fight-back's pick of its attacker (<c>TravelLeg.Attacker</c>, the skirmish's next attacker) is read off the board. A tusker that keeps pace is fought at once (the run past's own rule, as before) until its bolt
    /// kills it; the pick's lane and the leg's line of the shadow are gone.
    /// </summary>
    [Fact]
    public void The_fight_backs_pick_reads_off_the_threat_board_and_fights_the_tusker_that_keeps_pace_as_before()
    {
        var (t, context) = RunPastRig(anim: 3.0, seconds: 20);
        t.Travel.OnRun = (r, ran, arrived) =>
        {
            var here = t.R.H.State.Self.Position!.Value;
            if (ran == 0)
            {
                t.Attacker(Runner, "Armored Tusker", 3f, wcid: 11540);
                return;
            }

            if (t.R.H.State.Objects.TryGet(Runner, out _))
            {
                t.MoveObject(Runner, here.CellId, here.Origin + new Vector3(2, 0, 0)); // it runs beside her
            }
        };

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("fight-back: 'Armored Tusker' 0x80001960 is dead")); // the leg's own fight-back, as before
        Assert.Empty(t.Lines(ThreatShadowLane.Token)); // the pick's lane and the leg's line are gone
        Assert.Equal(0, ThreatBoard.For(t.Session).Lane("travel-fight-back").Agree + ThreatBoard.For(t.Session).Lane("travel-fight-back").Differ);
    }

    /// <summary>
    /// M6 16 Q2b: the one change of answer for the fight-back's pick. An Armored Tusker hits her in the portal network, before the run's leg starts its watch, and keeps pace beside her on the run. The leg's watch never heard it:
    /// before, the walk's present read (Q2, off the board) stopped her for it but the fight-back's pick (off the watch) found no attacker to fight; now the pick reads the board too and fights it until its bolt kills it.
    /// </summary>
    [Fact]
    public void A_travel_leg_whose_watch_began_after_an_attack_now_fights_the_remembered_attacker_that_keeps_pace()
    {
        var (t, context) = RunPastRig(anim: 3.0, seconds: 20);
        _ = ThreatBoard.For(t.Session); // the session's board hears from before the leg
        var runsFor = t.Travel.RunsFor!;
        t.Travel.RunsFor = r =>
        {
            if (r.Destination.Name == "network-to-refuge-front")
            {
                t.Attacker(Runner, "Armored Tusker", 3f, wcid: 11540);
            }

            return runsFor(r);
        };
        t.Travel.OnRun = (_, _, _) =>
        {
            var here = t.R.H.State.Self.Position!.Value;
            if (t.R.H.State.Objects.TryGet(Runner, out _))
            {
                t.MoveObject(Runner, here.CellId, here.Origin + new Vector3(2, 0, 0)); // it runs beside her
            }
        };

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("fight-back: 'Armored Tusker' 0x80001960 is dead")); // the remembered attacker, fought by the leg's own fight-back
        Assert.Empty(t.Lines(ThreatShadowLane.Token));
    }

    /// <summary>
    /// An Armored Tusker hits her in the portal network, 4 s before the run's leg starts its watch, and stands 30 m from her when the run to the entry's front begins. The leg's watch never heard it. Before M6 16 Q2 the leg read no
    /// attacker present and the shadow named the creature the board held; now the leg reads its attackers off the board, which heard it: the run past judges it (a thrower at 30 m that lands nothing: she runs past it), and the
    /// walk is never stopped or planned again.
    /// </summary>
    [Fact]
    public void A_travel_leg_whose_watch_began_after_an_attack_now_judges_the_attacker_the_board_holds_and_runs_past_it()
    {
        var (t, context) = RunPastRig(anim: 1.0, seconds: 12);
        _ = ThreatBoard.For(t.Session); // the session's board hears from before the leg
        var runsFor = t.Travel.RunsFor!;
        t.Travel.RunsFor = r =>
        {
            if (r.Destination.Name == "network-to-refuge-front")
            {
                t.Attacker(Runner, "Armored Tusker", 3f, wcid: 11540);
            }

            return runsFor(r);
        };
        t.Travel.OnRun = (_, ran, _) =>
        {
            if (ran == 0)
            {
                var here = t.R.H.State.Self.Position!.Value;
                t.MoveObject(Runner, here.CellId, here.Origin + new Vector3(0f, 30f, 0f));
            }
        };

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("run past: passing: 'Armored Tusker' 0x80001960 at 30.0 m")); // the remembered attacker, judged
        Assert.Empty(t.Lines("fight-back 1 on the run leg"));
        Assert.Single(t.Travel.Requests, r => r.Destination.Name == "entry-front"); // the walk was never stopped or planned again
        Assert.Empty(t.Lines(ThreatShadowLane.Token)); // the walk's lane is gone
    }
}
