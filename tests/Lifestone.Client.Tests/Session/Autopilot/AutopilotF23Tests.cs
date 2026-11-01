using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 F23, "never leave the fight under attack" (soak round 2's death: the autopilot stopped the den fight for a buff margin with a Tusker Guard on the mage; the upkeep's buff station then waited 50 s on the re-buff driver's
/// "in a fight" guard while the health fell from 110 to 1 of 195; the corpse trip's fight then had no walker). One test per item of the fix.
/// </summary>
public sealed class AutopilotF23Tests(ITestOutputHelper output)
{
    private const uint Guard = 0x80000F23;

    /// <summary>A living target creature of the ground <paramref name="metres"/> from the mage.</summary>
    private static void Hostile(ApWorld w, uint guid, float metres)
    {
        var at = w.Trip.View.State.Self.Position!.Value;
        var r = w.Trip.Vendor.H.R;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Frame.Origin.X + metres, at.Frame.Origin.Y, at.Frame.Origin.Z), name: "Tusker Guard");
        r.Reducer.OnCreateObject(r.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = w.Ground.TargetWcids()[0] } }, isUpdate: false);
    }

    private static void Gone(ApWorld w, uint guid)
    {
        var r = w.Trip.Vendor.H.R;
        r.Reducer.OnDeleteObject(r.Context, new DeleteObjectMsg(guid, 1));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Item1_the_upkeep_is_never_entered_for_a_buff_margin_while_a_hostile_is_in_reach_and_it_comes_once_the_reach_is_clear(string key)
    {
        var w = new ApWorld(key, output, atHome: false);
        ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded(); // every wanted line is under its margin in the scripted world
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        Hostile(w, Guard, 6); // a Tusker Guard 6 m away: inside the 20 m reach
        var until = w.Now + 60;
        w.Run(autopilot, a => w.Now >= until);

        Assert.DoesNotContain(arrows, a => a.To == AutopilotState.Upkeep);
        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Contains(w.Lines, l => l.Contains("the upkeep is deferred while Tusker Guard 0x80000F23 at 6 m", StringComparison.Ordinal));

        Gone(w, Guard); // the reach is clear: the upkeep comes at the next check
        w.Run(autopilot, a => a.State == AutopilotState.Upkeep);
        Assert.Contains(arrows, a => a.To == AutopilotState.Upkeep && a.Why.StartsWith("buff-margin: ", StringComparison.Ordinal));
    }

    /// <summary>A scripted fight that refreshes its own buffs (as <see cref="FightScenario"/> does): the autopilot asks it, never stops it.</summary>
    private sealed class RefreshingFight(ScriptedFight inner) : IStationRun, IStoppableFight, IBuffRefreshFight
    {
        public List<(double Margin, double Reach, string Detail)> Asked { get; } = [];

        public string Name => inner.Name;

        public string Stage => inner.Stage;

        public string? Failure => inner.Failure;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => inner.Ok;

        public int Kills => inner.Kills;

        public FightStopReason? StopReason => inner.StopReason;

        public bool BuffRefreshAsked { get; private set; }

        public int BuffRefreshes => 0;

        public void RequestStop(FightStopReason reason) => inner.RequestStop(reason);

        public void RequestBuffRefresh(double marginSeconds, double reachMetres, string detail)
        {
            Asked.Add((marginSeconds, reachMetres, detail));
            BuffRefreshAsked = true;
        }

        public ScenarioStatus Step(IGameSessionView session, double now) => inner.Step(session, now);

        public string VerdictFields() => inner.VerdictFields();
    }

    [Fact]
    public void Item1_a_fight_that_refreshes_its_buffs_is_asked_to_refresh_them_and_is_never_stopped_for_the_margin()
    {
        var w = new ApWorld(Den, output, atHome: false);
        ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded(); // every wanted line is under its margin
        var fights = new List<RefreshingFight>();
        w.Factory.WrapFight = f =>
        {
            var wrapped = new RefreshingFight(f);
            fights.Add(wrapped);
            return wrapped;
        };
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        var until = w.Now + 60;
        w.Run(autopilot, a => w.Now >= until);

        Assert.DoesNotContain(arrows, a => a.To == AutopilotState.Upkeep);
        Assert.Equal(AutopilotState.Fight, autopilot.State);
        var asked = Assert.Single(fights[^1].Asked); // asked once; the fight runs it between kills
        Assert.Equal(new BuffOptions().RebuffMarginSeconds, asked.Margin);
        Assert.Equal(DeathOptions.DefaultTripAggroMetres, asked.Reach);
        Assert.Contains("under the", asked.Detail, StringComparison.Ordinal);
        Assert.Contains(w.Lines, l => l.Contains("the fight refreshes it between kills with no hostile in reach (F23); the fight is not stopped", StringComparison.Ordinal));
        Assert.Contains(typeof(IBuffRefreshFight), typeof(FightScenario).GetInterfaces()); // the den fight is the real refresher
    }

    [Fact]
    public void Item1_the_fight_refreshes_the_lines_between_kills_only_when_no_hostile_is_in_reach()
    {
        var rig = new FightManaTests.Rig(mana: 400, stamina: 200, fight: new FightOptions { Kills = 3 }) { HitsToKill = 2 };
        rig.Caster.ManaRegenPerSecond = 40;
        var near = rig.AddTusker(12); // within the 20 m reach
        rig.AddTusker(60); // the next target, out of the reach: the fight goes on after the first kill
        FightF24Tests.Prime(rig, l => l.Name == "Quickness Self" ? 100 : 1500); // F24: a buffed mage with one line due (an unbuffed one would be recast at once, whatever is in reach); F25: 100 s is inside the lookahead's 150 s (the seed 90 + 60)
        var scenario = rig.Scenario();
        scenario.RequestBuffRefresh(300, 20, "Quickness Self under the 300 s margin"); // asked with the tusker 12 m away
        rig.Run(scenario, 120, until: () => ((IStoppableFight)scenario).Kills >= 1);
        foreach (var line in rig.Log.Lines.Where(l => l.Contains("refresh", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        Assert.True(((IStoppableFight)scenario).Kills >= 1, "the near tusker was killed first");
        Assert.Contains(rig.Log.Lines, l => l.Contains("buff refresh: waits: Tusker Slave 0x" + near.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var buffsBefore = rig.Caster.Answered.Count(a => !FightManaTests.Rig.IsBolt(a.Spell));

        rig.Run(scenario, 120, until: () => scenario.BuffRefreshes >= 1);
        Assert.Equal(1, scenario.BuffRefreshes);
        Assert.False(scenario.BuffRefreshAsked);
        var started = rig.Log.Lines.First(l => l.Contains("buff refresh: lookahead: Quickness Self ", StringComparison.Ordinal)); // M5 7 F25: the lull's lookahead
        Assert.Contains("no hostile within 20 m", started, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Lines, l => l.Contains("buff refresh: done (", StringComparison.Ordinal));
        Assert.True(rig.Caster.Answered.Count(a => !FightManaTests.Rig.IsBolt(a.Spell)) > buffsBefore, "the pass cast buff lines");
        Assert.Null(scenario.StopReason); // never stopped for it
    }

    [Fact]
    public void Item2_the_guard_heals_at_once_in_the_upkeep_state_under_the_emergency_line_with_the_fights_heal_pick()
    {
        var rig = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true);
        AutopilotF30Tests.Settle(rig); // M5 7 F30: the guard waits for the login to settle (the player present)
        rig.Caster.HealthPerCast = 20;
        rig.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        rig.Caster.SetHealth(max * 30 / 100);
        rig.R.Tick();
        var ground = rig.Fight.GroundProfile;
        using var watch = new HostileWatch(rig.View, ground, 20);
        var guard = new AutopilotVitalsGuard(rig.Caster.Table, ground, new AutopilotOptions(), 0.9, watch, rig.Log);
        var held = false;
        var end = rig.Now + 30;
        while (rig.Now < end && !rig.Caster.Answered.Any(a => VitalsMeans.HealSelf.Contains(a.Spell)))
        {
            rig.R.Tick(0.1);
            rig.Caster.Pump();
            rig.R.Tick();
            held |= guard.Step(rig.View, rig.Now, "upkeep");
        }

        Assert.True(held, "the guard held the upkeep state");
        Assert.Contains(rig.Caster.Answered, a => VitalsMeans.HealSelf.Contains(a.Spell));
        var pick = rig.Log.Lines.First(l => l.Contains("guard: upkeep: heal pick:", StringComparison.Ordinal));
        Assert.Contains("vs Heal Self h/s=", pick, StringComparison.Ordinal); // the fight's one comparison (HealPick.Line)
        Assert.Contains("no target of known health in reach", pick, StringComparison.Ordinal);

        // at the safe line the guard lets the state go on
        rig.Caster.SetHealth(max);
        rig.R.Tick();
        for (var i = 0; i < 60 && guard.Step(rig.View, rig.Now, "upkeep"); i++)
        {
            rig.R.Tick(0.1);
            rig.Caster.Pump();
            rig.R.Tick();
        }

        Assert.False(guard.Holding);
    }

    [Fact]
    public void Item2_above_the_line_the_guard_never_holds_a_state()
    {
        var rig = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true);
        AutopilotF30Tests.Settle(rig); // M5 7 F30
        rig.R.Tick();
        var ground = rig.Fight.GroundProfile;
        using var watch = new HostileWatch(rig.View, ground, 20);
        var guard = new AutopilotVitalsGuard(rig.Caster.Table, ground, new AutopilotOptions(), 0.9, watch, rig.Log);
        for (var i = 0; i < 40; i++)
        {
            rig.R.Tick(0.1);
            Assert.False(guard.Step(rig.View, rig.Now, "trip"));
        }

        Assert.Equal(0, guard.Casts);
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("guard: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Item3_the_driver_of_the_autopilots_own_station_is_never_held_by_the_fight_guard_and_a_persons_driver_still_is()
    {
        var t = new RebuffRig(new BuffOptions { RebuffMarginSeconds = RebuffRig.Margin, DriverUnattended = true });
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(RebuffRig.Margin - 2);
        t.Hit(); // a Tusker Guard hit the mage a moment ago
        var hitAt = t.Now;
        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1, 30));
        Assert.True(t.Now - hitAt < RebuffDriver.FightQuietSeconds, "the autopilot's driver passed inside the 10 s after the hit");
        Assert.NotEqual(RebuffDriver.GuardFight, t.Driver.WaitingFor);

        var person = new RebuffRig();
        Assert.Equal(BuffStatus.Done, person.BuildBuffs());
        person.SkipTo(RebuffRig.Margin - 2);
        person.Hit();
        person.Advance(5);
        Assert.Equal(RebuffDriver.GuardFight, person.Driver.WaitingFor); // a person's driver keeps its guard

        Assert.Equal("yes", AutopilotScenario.BuffSpec.Arguments[JourneyStations.UnattendedArgument]); // the autopilot's buff and upkeep stations are its own
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Item3_an_upkeep_that_casts_nothing_for_20_s_with_a_hostile_in_reach_ends_by_name_and_the_fight_goes_on(string key)
    {
        var w = new ApWorld(key, output, atHome: false);
        ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded();
        var buffs = 0;
        w.Factory.Wedge = spec => spec.Kind == StationKinds.Buff && ++buffs >= 2; // the upkeep's buff station stands still, as soak round 2's did
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Upkeep);
        var upkeepAt = w.Now;
        Hostile(w, Guard, 5); // a Tusker Guard walks up during the upkeep
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.InRange(w.Now - upkeepAt, AutopilotScenario.UpkeepNoCastSeconds, AutopilotScenario.UpkeepNoCastSeconds + 2);
        Assert.Contains(w.Lines, l => l.Contains("autopilot upkeep: watchdog: the buff station made no cast for 20 s while Tusker Guard 0x80000F23", StringComparison.Ordinal));
        Assert.Contains(arrows, a => a.From == AutopilotState.Upkeep && a.To == AutopilotState.Fight && a.Why.Contains(AutopilotScenario.UpkeepNoCastName, StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("watchdog: upkeep over", StringComparison.Ordinal)); // its own watchdog, not the state's 300 s bound
    }
}
