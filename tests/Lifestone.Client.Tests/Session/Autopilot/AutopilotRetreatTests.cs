using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 6 F2 (<c>m5-fight-death-midrun</c>, map-death.md section 5): the retreat on the ground over the scripted ACE of <see cref="AutopilotRig"/>. Proved: the rule is asked in the fight (under the line and losing: the incoming beats the
/// payable heal) and in the upkeep heal; the retreat is the way home (no keeper's pass first), then the keeper at home, then the known state goes back out; <c>retreats=</c> and the <c>retreat</c> trigger reach the cycle and done lines,
/// with the vitae ledger's fields; the second retreat from one ground in one cycle stops <c>retreat:again:&lt;ground&gt;</c>; a ground whose line is 0 never retreats; the guard's nothing-payable answer is the rule
/// (<c>f23-guard-nothing-paid</c>).
/// </summary>
public sealed class AutopilotRetreatTests(ITestOutputHelper output)
{
    private const string Attacker = "Tusker Guard";

    private sealed class Hurt(ApWorld w)
    {
        private byte _sequence;

        public void Health(uint points) => w.Trip.Vendor.R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_sequence, 2, points));

        /// <summary>One melee hit on the mage (the HostileWatch's incoming rate; the health itself is set by <see cref="Health"/>).</summary>
        public void Hit() => w.Trip.Vendor.R.Server((p, seq) => AceM3Writers.DefenderNotification(p, seq, Attacker, 4, 0.1, 6, 4, false, 0));
    }

    /// <summary>A world on the ground whose vitals are read (each maximum 100), as the health-floor test makes it.</summary>
    private ApWorld World(string key, out Hurt hurt)
    {
        var w = new ApWorld(key, output, atHome: false);
        var r = w.Trip.Vendor.H.R;
        r.Reducer.OnPlayerDescription(
            r.Context,
            new GameEventHeader(WorldActionRig.Player, 90, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Inventory = [.. w.Trip.View.State.Self.Inventory.Pack.Select(e => new ContentDesc(e.Guid, e.ContainerType))], Vitals = [new VitalDesc(1, 100, 0, 0, 100), new VitalDesc(3, 100, 0, 0, 100), new VitalDesc(5, 100, 0, 0, 100)] });
        hurt = new Hurt(w);
        return w;
    }

    private static bool Retreated(List<AutopilotTransition> arrows) => arrows.Any(a => a.To == AutopilotState.Travel && a.Why.StartsWith("retreat: ", StringComparison.Ordinal));

    [Fact]
    public void The_retreat_line_is_the_profiles_or_the_derived_one_and_a_zero_line_never_retreats()
    {
        Assert.Equal(0, AutopilotScenario.RetreatLineFor(GroundCatalog.Get(Den), out var den));
        Assert.Contains("fight.retreat_health_percent: never", den, StringComparison.Ordinal);
        Assert.Equal((int)AutopilotScenario.HealthFloorPercent, AutopilotScenario.RetreatLineFor(GroundCatalog.Get(Temple), out var temple));
        Assert.StartsWith("derived: ", temple, StringComparison.Ordinal); // the same derived line the run leg's retreat reads (TravelLegSafety)
    }

    [Fact]
    public void A_losing_fight_under_the_line_retreats_home_heals_at_home_goes_back_out_and_counts_the_retreat()
    {
        var w = World(Temple, out var hurt);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        var healed = false;
        w.Run(
            autopilot,
            a => a.State == AutopilotState.Fight && arrows.Any(t => t.To == AutopilotState.KnownState && t.Why.StartsWith("after the retreat", StringComparison.Ordinal)),
            perFrame: a =>
            {
                if (a.State == AutopilotState.Fight && !Retreated(arrows) && a.CurrentRun is IStoppableFight { Kills: >= 2 })
                {
                    hurt.Health(40); // under the derived 50% line, over the upkeep's 30% floor: only the rule can stop the fight
                    hurt.Hit();
                }
                else if (a.State == AutopilotState.Upkeep && Retreated(arrows) && !healed)
                {
                    healed = true;
                    hurt.Health(100); // the keeper's pass at home (the scripted world has no heal)
                }
            });
        w.Trip.Vendor.R.Raw(AceWireWriters.ServerMessage(DeathScenario.VitaeReducedLine, 7)); // one step of the vitae worked off
        w.Trip.Vendor.R.Tick();
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));

        var kinds = Kinds(w);
        var retreat = arrows.First(a => a.To == AutopilotState.Travel && a.Why.StartsWith("retreat: ", StringComparison.Ordinal));
        Assert.Contains("incoming-beats-heal at health 40/100 under the 50% line (fight)", retreat.Why, StringComparison.Ordinal);
        Assert.Equal(AutopilotState.Fight, retreat.From);
        var at = arrows.IndexOf(retreat);
        Assert.Equal(AutopilotState.Upkeep, arrows[at + 1].To); // the way home, then the keeper at home
        Assert.StartsWith("retreat: home (", arrows[at + 1].Why, StringComparison.Ordinal);
        Assert.Equal(AutopilotState.KnownState, arrows[at + 2].To); // then the known state, which goes back out
        Assert.Contains(arrows.Skip(at + 2), a => a.To == AutopilotState.Fight);
        var home = kinds.IndexOf("recall-home");
        Assert.True(home > kinds.IndexOf("den-fight") && kinds.Skip(home + 1).Contains("den-fight"), string.Join(",", kinds)); // the fight, the way home, the fight again
        Assert.Contains(w.Lines, l => l.StartsWith("retreat: decide health=40% ", StringComparison.Ordinal) && l.Contains("-> retreat", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("stop asked (retreat)", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.Retreats);
        var cycle = autopilot.Lines[0];
        Assert.StartsWith("autopilot cycle n=1 state=stopped why=retreat:1,", cycle, StringComparison.Ordinal);
        Assert.Equal(1, Int(cycle, "retreats"));
        var done = autopilot.Lines[^1];
        Assert.Equal(1, Int(done, "retreats"));
        Assert.Equal(("none", 1), (Field(done, "vitae"), Int(done, "vitae_steps"))); // the vitae ledger's fields close the done line
        Assert.EndsWith(" " + autopilot.VitaeFields, done, StringComparison.Ordinal);
    }

    [Fact]
    public void The_second_retreat_from_one_ground_in_one_cycle_stops_by_name()
    {
        var w = World(Temple, out var hurt);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var status = w.Run(
            autopilot,
            perFrame: a =>
            {
                if (a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 })
                {
                    hurt.Health(40);
                    hurt.Hit();
                }
                else if (a.State == AutopilotState.Upkeep)
                {
                    hurt.Health(100);
                }
            });

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(AutopilotScenario.RetreatAgainPrefix + Temple, autopilot.StopName);
        Assert.Contains(w.Lines, l => l.Contains("TODO(m6-retreat-rung-drop)", StringComparison.Ordinal));
        Assert.Equal(2, autopilot.Retreats);
        Assert.Equal(2, Int(autopilot.Lines[^1], "retreats"));
        Assert.Equal(0, autopilot.Cycles); // both in the first cycle (a retreat home ends no cycle)
    }

    [Fact]
    public void The_upkeep_heal_that_cannot_outheal_the_incoming_retreats_home()
    {
        var w = World(Temple, out var hurt);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(
            autopilot,
            a => arrows.Any(t => t.To == AutopilotState.KnownState && t.Why.StartsWith("after the retreat", StringComparison.Ordinal)),
            perFrame: a =>
            {
                if (a.State == AutopilotState.Fight && !Retreated(arrows) && a.CurrentRun is IStoppableFight { Kills: >= 2 })
                {
                    hurt.Health(20); // under the upkeep's floor: the heal in place; no hit yet, so the rule holds in the fight
                }
                else if (a.State == AutopilotState.Upkeep && !Retreated(arrows))
                {
                    hurt.Hit(); // the heal in place is hit: the keeper cannot outheal it
                }
                else if (a.State == AutopilotState.Upkeep)
                {
                    hurt.Health(100);
                }
            });

        Assert.Contains(arrows, a => a.To == AutopilotState.Upkeep && a.Why.StartsWith("health-floor: ", StringComparison.Ordinal));
        var retreat = arrows.First(a => a.To == AutopilotState.Travel && a.Why.StartsWith("retreat: ", StringComparison.Ordinal));
        Assert.Equal(AutopilotState.Upkeep, retreat.From);
        Assert.Contains("(upkeep heal)", retreat.Why, StringComparison.Ordinal);
        Assert.Equal(1, autopilot.Retreats);
    }

    /// <summary>
    /// M6 14 holding safety: on the Holding, whose Rampagers carry their attack's facts and whose profile names no line, the run's line is derived on her live state (no armour and no protection read in the scripted world: the top, 95%):
    /// a fight at 90 health of 100 with a hit coming in retreats under the derived line, where the general 50% line would have fought on; the upkeep's floor is the same line held at the keeper's 80% (a floor over the heal's target could
    /// never be met), so at 90 only the retreat rule stops the fight; the line is logged with its numbers.
    /// </summary>
    [Fact]
    public void On_the_Holding_the_derived_line_stops_a_fight_the_general_line_would_have_fought_on()
    {
        var w = World("tusker-holding", out var hurt);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(
            autopilot,
            _ => Retreated(arrows),
            perFrame: a =>
            {
                if (a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 })
                {
                    hurt.Health(90); // over the general 50% line and its 30% floor, and over the keeper's 80%
                    hurt.Hit();
                }
            });

        var stop = arrows.First(a => a.To == AutopilotState.Travel && a.Why.StartsWith("retreat: ", StringComparison.Ordinal));
        Assert.Equal(AutopilotState.Fight, stop.From);
        Assert.Contains("at health 90/100 under the 95% line (fight)", stop.Why, StringComparison.Ordinal);
        Assert.DoesNotContain(arrows, a => a.Why.StartsWith("health-floor: ", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("fight line: retreat 95% derived on tusker-holding: 1 x Rampager's hit 293", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("fight.retreat_health_percent", StringComparison.Ordinal) && l.Contains("is ignored", StringComparison.Ordinal)); // the Holding's profile names none now
    }

    [Fact]
    public void A_ground_whose_line_is_zero_never_retreats()
    {
        var w = World(Den, out var hurt);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        var until = double.NaN;
        w.Run(
            autopilot,
            a => !double.IsNaN(until) && w.Now >= until,
            perFrame: a =>
            {
                if (a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 })
                {
                    until = double.IsNaN(until) ? w.Now + 30 : until;
                    hurt.Health(20);
                    hurt.Hit();
                }
            });

        Assert.False(double.IsNaN(until));
        Assert.False(Retreated(arrows));
        Assert.Equal(0, autopilot.Retreats);
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("retreat: ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_guard_with_nothing_payable_under_its_line_asks_the_retreat_rule_and_says_so()
    {
        var rig = new FightManaTests.Rig(mana: 0, stamina: 200);
        AutopilotF30Tests.Settle(rig);
        rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        rig.Caster.SetHealth(max * 30 / 100);
        rig.R.Tick();
        var ground = rig.Fight.GroundProfile;
        using var watch = new HostileWatch(rig.View, ground, 20);
        var guard = new AutopilotVitalsGuard(rig.Caster.Table, ground, new AutopilotOptions(), 0.9, watch, rig.Log);
        var end = rig.Now + 30;
        while (rig.Now < end && !guard.NothingPaid)
        {
            rig.R.Tick(0.1);
            rig.Caster.Pump();
            rig.R.Tick();
            guard.Step(rig.View, rig.Now, "upkeep");
        }

        Assert.True(guard.NothingPaid);
        Assert.False(guard.Holding); // the state goes on: the autopilot answers with the rule (GuardRetreat)
        Assert.Equal(0, guard.Casts);
        Assert.True(guard.AssessHeal(rig.View, out var heal, out var payable));
        Assert.Equal((0.0, false), (heal, payable));
        Assert.Contains(rig.Log.Lines, l => l.Contains("no Heal Self and no drain is paid for (mana ", StringComparison.Ordinal) && l.Contains("): the retreat rule is asked (M6 6 F2", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("TODO(f23-guard-nothing-paid)", StringComparison.Ordinal));
        Assert.False(guard.EmergencyAside(rig.Now));
    }

    [Fact]
    public void The_upkeep_heal_that_ends_under_the_line_with_nothing_payable_takes_the_retreat_not_the_preconditions_failure()
    {
        // M6 6 FXM2 (6-M8): no hit comes in; the keeper's pass in place ends with the health still under the line and nothing payable (the scripted world has no heal). The window for the rule's hold runs from the heal's first ask.
        var w = World(Temple, out var hurt);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(
            autopilot,
            a => arrows.Any(t => t.To == AutopilotState.KnownState && t.Why.StartsWith("after the retreat", StringComparison.Ordinal)),
            perFrame: a =>
            {
                if (a.State == AutopilotState.Fight && !Retreated(arrows) && a.CurrentRun is IStoppableFight { Kills: >= 2 })
                {
                    hurt.Health(20); // under the upkeep's floor and the line: the heal in place; no hit
                }
                else if (a.State == AutopilotState.Upkeep && Retreated(arrows))
                {
                    hurt.Health(100);
                }
            });

        var retreat = arrows.First(a => a.To == AutopilotState.Travel && a.Why.StartsWith("retreat: ", StringComparison.Ordinal));
        Assert.Equal(AutopilotState.Upkeep, retreat.From);
        Assert.Contains("the keeper's pass in place ended under the line", retreat.Why, StringComparison.Ordinal);
        Assert.Equal(1, autopilot.Retreats);
        Assert.DoesNotContain(arrows, a => a.Why.Contains("failed:upkeep:precondition:health", StringComparison.Ordinal));
    }
}
