using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 16 E2a (docs/automation-engine.md E2 and section 3, "shadow first"; m6-plan.md D139): the outer loop's part of the threat board's shadow (<c>AutopilotScenario.ThreatShadow.cs</c>). The guard's read
/// decides as before; the session's board is asked the same question beside them; the cycle line closes with the cycle's counts; a difference is one line. Report-only. M6 16 Q2: the hostile watch's own question
/// and the retreat line's are the board's, and their lanes are gone.
/// </summary>
public sealed class AutopilotThreatShadowTests(ITestOutputHelper output)
{
    private const uint Guard = 0x80000E2A;

    private static List<string> Shadow(ApWorld w) => [.. w.Lines.Where(l => l.Contains(ThreatShadowLane.Token, StringComparison.Ordinal))];

    /// <summary>A living target creature of the ground <paramref name="metres"/> from the mage.</summary>
    private static void Hostile(ApWorld w, uint guid, float metres)
    {
        var at = w.Trip.View.State.Self.Position!.Value;
        var r = w.Trip.Vendor.H.R;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Origin.X + metres, at.Origin.Y, at.Origin.Z), name: "Tusker Guard");
        r.Reducer.OnCreateObject(r.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = w.Ground.TargetWcids()[0], ObjectDescriptionFlags = WorldObjectTable.AttackableFlag } }, isUpdate: false);
    }

    [Fact]
    public void The_cycle_line_closes_with_the_cycles_counts_and_a_run_whose_watch_and_board_heard_the_same_agrees_throughout()
    {
        var w = new ApWorld(Den, output);
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 3 });
        w.Run(autopilot, a => a.Cycles == 1 && a.State == AutopilotState.Fight);
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));

        var cycles = autopilot.Lines.Where(l => l.StartsWith("autopilot cycle ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, cycles.Count);
        Assert.All(cycles, l => Assert.EndsWith(" threat_shadow_agree=" + Int(l, "threat_shadow_agree") + " threat_shadow_differ=0", l, StringComparison.Ordinal));
        var board = ThreatBoard.For(w.Trip.View);
        var guard = board.Lane(AutopilotVitalsGuard.AttackersDetector);
        Assert.Equal((long)cycles.Sum(l => Int(l, "threat_shadow_agree")), guard.Agree); // each cycle's line holds its own share of the outer loop's lane (E2b; M6 16 Q2: the watch's and the retreat's lanes are gone): together, the session's
        Assert.True(guard.Agree > 0, $"the guard {guard.Agree}");
        Assert.Equal(0, guard.Differ);
        Assert.Empty(Shadow(w));
    }

    /// <summary>M6 16 Q2: the start-up audit of a scenario rebuilt after a hit (its attacker 30 m off, outside the reach) calls it attacked, as InReach does; before, its own watch's clock made it "in-reach".</summary>
    [Fact]
    public void A_scenario_rebuilt_after_a_hit_audits_its_hostiles_as_attacked()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var first = w.Autopilot(new AutopilotOptions());
        w.Run(first, a => a.State == AutopilotState.Fight);
        Hostile(w, Guard, 30);
        w.Trip.Vendor.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseHit, "Tusker Guard", 1, 12, 0.06, false, 4, false, 0, "Tusker Guard hits you for 12 points of slashing damage!"));
        var until = w.Now + 2;
        w.Run(first, _ => w.Now >= until);
        first.Dispose();

        var before = w.Lines.Count();
        var second = w.Autopilot(new AutopilotOptions());
        until = w.Now + 1;
        w.Run(second, _ => w.Now >= until);

        Assert.Contains(w.Lines.Skip(before), l => l.StartsWith("autopilot audit hostiles: attacked: Tusker Guard attacked ", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines.Skip(before), l => l.StartsWith("autopilot audit hostiles: in-reach: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_scenario_rebuilt_after_a_hit_knows_the_hit_the_sessions_board_holds()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var first = w.Autopilot(new AutopilotOptions());
        w.Run(first, a => a.State == AutopilotState.Fight);
        Hostile(w, Guard, 30); // a Tusker Guard 30 m away, outside the 20 m reach, hits her
        w.Trip.Vendor.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseHit, "Tusker Guard", 1, 12, 0.06, false, 4, false, 0, "Tusker Guard hits you for 12 points of slashing damage!"));
        var until = w.Now + 2;
        w.Run(first, _ => w.Now >= until);
        Assert.Empty(Shadow(w)); // the watch that heard the hit and the board agree, and so do the guard's attackers on her (E2b)
        first.Dispose(); // a rung move: the scenario, its watch and its hit history go

        var before = w.Lines.Count();
        var second = w.Autopilot(new AutopilotOptions());
        until = w.Now + 1;
        w.Run(second, _ => w.Now >= until);

        // M6 16 Q2: before, the rebuilt scenario's watch had heard nothing and its start read no hostile (the shadow named the hit the board held); now its watch asks the board and the start fights back first
        Assert.Contains(w.Lines.Skip(before), l => l.Contains("autopilot state known-state -> fight (cycle 1): audit: a hostile is on it (Tusker Guard attacked ", StringComparison.Ordinal));
        Assert.Empty(Shadow(w));
    }
}
