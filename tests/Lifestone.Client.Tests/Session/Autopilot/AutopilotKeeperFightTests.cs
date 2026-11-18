using Lifestone.Client.Session.Autopilot;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 7 BD (live, 2026-11-07 03:34:49 to 03:37:53 UTC: a logistics trip asked in the Lodge with tuskers on her; the Trip state's keeper rested and danced through 56 hits, no bolt, to her death): the one rule. In any state's keeper phase and any station's
/// wait, on a ground, with a hostile on her, the keeper stops at once, she fights back until no hostile is in reach, then the state resumes. One test a state; the stand-downs; the trip's wait for the lull and the urgent recall.
/// </summary>
public sealed class AutopilotKeeperFightTests(ITestOutputHelper output)
{
    private const uint Guard = 0x80000BD1;

    private const string Stops = "autopilot keeper: check before:";

    private static void Hostile(ApWorld w, float metres = 6)
    {
        var at = w.Trip.View.State.Self.Position!.Value;
        var r = w.Trip.Vendor.H.R;
        var basis = ReducerHarness.Create(Guard, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Frame.Origin.X + metres, at.Frame.Origin.Y, at.Frame.Origin.Z), name: "Tusker Guard");
        r.Reducer.OnCreateObject(r.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = w.Ground.TargetWcids()[0] } }, isUpdate: false);
    }

    private static void Gone(ApWorld w)
    {
        var r = w.Trip.Vendor.H.R;
        r.Reducer.OnDeleteObject(r.Context, new DeleteObjectMsg(Guard, 1));
    }

    private static void Hit(ApWorld w) =>
        w.Trip.Vendor.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseHit, "Male Tusker", 4, 5, 0.1, false, 4, false, 0, "Male Tusker grazes your lower arm for 5 points of bludgeoning damage!"));

    private static ScriptedRun Trip(StationSpec spec) => new(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=3 bought=2");

    /// <summary>Asserts the rule's arc: the state went to the fight by the rule, and came back when no hostile was in reach.</summary>
    private static void FoughtBackAndResumed(ApWorld w, List<AutopilotTransition> arrows, AutopilotState state)
    {
        Assert.Contains(w.Lines, l => l.StartsWith(Stops, StringComparison.Ordinal) && l.Contains("a hostile is on her", StringComparison.Ordinal));
        Assert.Contains(arrows, a => a.From == state && a.To == AutopilotState.Fight && a.Why.Contains("she fights back (D88), then the state resumes", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("stop asked (keeper-fight-back)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot keeper: the fight-back is over after ", StringComparison.Ordinal));
        Assert.Contains(arrows, a => a.From == AutopilotState.Fight && a.To == state && a.Why.StartsWith("the keeper's fight-back is over: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Trip_a_hostile_in_the_keepers_phase_stops_it_the_fight_answers_and_the_trip_resumes_with_no_station_started_before_it()
    {
        var w = new ApWorld(Den, output);
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? Trip(spec) : null;
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 2 });
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Trip);
        Hostile(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.DoesNotContain(w.Lines, l => l.Contains("autopilot trip: station logistics starts", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot fight-back: a hostile is on her or in reach (Tusker Guard", StringComparison.Ordinal));
        var until = w.Now + 30;
        w.Run(autopilot, a => w.Now >= until);
        Assert.Equal(AutopilotState.Fight, autopilot.State); // until no hostile is in reach

        Gone(w);
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("autopilot trip: station logistics starts", StringComparison.Ordinal)));
        FoughtBackAndResumed(w, arrows, AutopilotState.Trip);
    }

    [Fact]
    public void Trip_an_attack_alone_with_no_creature_in_reach_stops_the_keeper_and_the_fight_ends_when_the_attacks_stop()
    {
        var w = new ApWorld(Den, output);
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? Trip(spec) : null;
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 2 });
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Trip);
        Hit(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("autopilot trip: station logistics starts", StringComparison.Ordinal)));

        FoughtBackAndResumed(w, arrows, AutopilotState.Trip);
    }

    /// <summary>A world on the ground whose vitals are read (each maximum 100), as the health-floor test makes it.</summary>
    private static void ReadVitals(ApWorld w)
    {
        var r = w.Trip.Vendor.H.R;
        r.Reducer.OnPlayerDescription(
            r.Context,
            new GameEventHeader(WorldActionRig.Player, 90, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Inventory = [.. w.Trip.View.State.Self.Inventory.Pack.Select(e => new ContentDesc(e.Guid, e.ContainerType))], Vitals = [new VitalDesc(1, 100, 0, 0, 100), new VitalDesc(3, 100, 0, 0, 100), new VitalDesc(5, 100, 0, 0, 100)] });
    }

    private static void Vital(ApWorld w, ref byte sequence, uint id, uint points) => w.Trip.Vendor.R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(++sequence, id, points));

    [Fact]
    public void Buff_the_real_keepers_pass_on_the_ground_is_halted_by_name_and_the_state_resumes()
    {
        ApWorld? world = null;
        var w = new ApWorld(Den, output, atHome: false) { KeeperMaker = c => new VitalsKeeper(world!.Trip.View, c.Spells, new VitalsOptions(), c.Log) };
        world = w;
        ReadVitals(w);
        byte sequence = 0;
        Vital(w, ref sequence, 6, 1); // no mana: the buff's need puts the keeper to work
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Buff);
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("vitals: needs ", StringComparison.Ordinal)), 40);
        Hostile(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Contains(w.Lines, l => l.Contains("vitals: pass halted: a hostile is on her (Tusker Guard", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("autopilot buff: station buff starts", StringComparison.Ordinal));
        Gone(w);
        w.Run(autopilot, a => a.State == AutopilotState.Buff);
        FoughtBackAndResumed(w, arrows, AutopilotState.Buff);
    }

    [Fact]
    public void Upkeep_the_heal_in_place_fights_back_first_then_resumes_and_ends_as_ever()
    {
        var w = new ApWorld(Temple, output, atHome: false);
        ReadVitals(w);
        byte sequence = 0;
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });
        Vital(w, ref sequence, 2, 20); // under the upkeep's floor
        w.Run(autopilot, a => a.State == AutopilotState.Upkeep);
        Vital(w, ref sequence, 2, 60); // the heal is under way, over the ground's retreat line: under it the retreat rule decides (AutopilotRetreatTests)
        Hit(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        Vital(w, ref sequence, 2, 100); // the fight's own heal
        w.Run(autopilot, a => a.State == AutopilotState.Upkeep);
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("the keeper restored the health", StringComparison.Ordinal)));

        Assert.Contains(arrows, a => a.From == AutopilotState.Fight && a.To == AutopilotState.Upkeep && a.Why == "the keeper's fight-back is over: the heal in place resumes");
    }

    [Fact]
    public void Travel_a_recovery_home_on_the_ground_fights_back_first_then_goes_home()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var failed = false;
        w.Factory.FailWith = spec =>
        {
            if (spec.Kind != StationKinds.DenFight || failed)
            {
                return null;
            }

            failed = true;
            return "scripted";
        };
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Travel);
        Hostile(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        Gone(w);
        w.Run(autopilot, a => a.State == AutopilotState.Travel);

        FoughtBackAndResumed(w, arrows, AutopilotState.Travel);
        Assert.Equal(2, w.Factory.Made.Count(s => s.Kind == StationKinds.RecallHome)); // made again from its start; the first never ran
    }

    [Fact]
    public void Buff_a_hostile_before_the_ground_pass_fights_back_first_then_the_pass_runs()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Buff);
        Hostile(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        Gone(w);
        w.Run(autopilot, a => a.State == AutopilotState.Buff);

        FoughtBackAndResumed(w, arrows, AutopilotState.Buff);
    }

    [Fact]
    public void Upkeep_a_hostile_before_the_upkeeps_station_fights_back_first_then_the_upkeep_resumes()
    {
        var w = new ApWorld(Den, output, atHome: false);
        ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded(); // every wanted line is under its margin in the scripted world
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Upkeep);
        Hit(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        w.Run(autopilot, a => a.State == AutopilotState.Upkeep);

        FoughtBackAndResumed(w, arrows, AutopilotState.Upkeep);
    }

    [Fact]
    public void Death_a_resumed_death_stations_phase_on_the_ground_fights_back_first_then_the_station_runs()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var autopilot = w.Autopilot(new AutopilotOptions(), seed: new AutopilotSeed(1, 0, PendingDeath: new DeathTally()));
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Death);
        Hostile(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        Gone(w);
        w.Run(autopilot, a => a.State == AutopilotState.Death);

        FoughtBackAndResumed(w, arrows, AutopilotState.Death);
    }

    [Fact]
    public void Death_a_scripted_death_is_left_alone_it_is_there_to_die()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var autopilot = w.Autopilot(new AutopilotOptions { TestDeath = true });
        w.Run(autopilot, a => a.State == AutopilotState.Death);
        Hostile(w);
        w.Run(autopilot, a => a.State != AutopilotState.Death, 400);

        Assert.DoesNotContain(w.Lines, l => l.StartsWith(Stops, StringComparison.Ordinal));
    }

    [Fact]
    public void Fight_a_hostile_in_the_fights_own_phase_starts_it_at_once_and_no_state_waits()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        Hostile(w);
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("autopilot fight: station den-fight starts", StringComparison.Ordinal)));
        var until = w.Now + 20;
        w.Run(autopilot, a => w.Now >= until);

        Assert.Contains(arrows, a => a.From == AutopilotState.Fight && a.To == AutopilotState.Fight && a.Why.Contains("the fight starts at once (D88)", StringComparison.Ordinal));
        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.DoesNotContain(w.Lines, l => l.Contains("stop asked (keeper-fight-back)", StringComparison.Ordinal));
    }

    /// <summary>A station that fails on a refused cast for mana, so the recovery table starts its mana dance.</summary>
    private sealed class ManaRefused(ApWorld w, string name) : IStationRun
    {
        public string Name => name;

        public string Stage => "scripted";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => false;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (Failure is null)
            {
                var outcome = new ActionOutcome(ActionOutcomeKind.Refused, 0, "not enough mana: the cast needs more", ActionReason.NotEnoughMana);
                w.Trip.Vendor.H.ActionLog.Add(new ActionLogEntry(9000 + session.ActionLog.Total, ActionKind.SetCombatMode, ActionSource.Automation, "test", outcome, 0, 0));
                Failure = "cast: " + outcome.Kind + ": " + outcome.Detail;
            }

            return ScenarioStatus.Failed;
        }

        public string VerdictFields() => string.Empty;
    }

    [Fact]
    public void M6_7_finish_the_recoverys_mana_dance_gives_way_to_a_hostile_and_the_state_resumes()
    {
        ApWorld? world = null;
        var w = new ApWorld(Den, output) { KeeperMaker = c => new VitalsKeeper(world!.Trip.View, c.Spells, new VitalsOptions(), c.Log) };
        world = w;
        ReadVitals(w);
        byte sequence = 0;
        w.Factory.Script = spec =>
        {
            if (spec.Kind != StationKinds.Logistics)
            {
                return null;
            }

            Vital(w, ref sequence, 6, 1); // the mana is gone as the station starts, so the fix has a dance to do
            return new ManaRefused(w, spec.Name);
        };
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 2 });
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, _ => w.Lines.Any(l => l.StartsWith("autopilot recovery: reason=NotEnoughMana fix=mana-dance after", StringComparison.Ordinal)));
        Hostile(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Contains(w.Lines, l => l.StartsWith(Stops + " the recovery's mana dance (reason=NotEnoughMana) of the trip state", StringComparison.Ordinal) && l.Contains("the fix and its retry are dropped", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("fix=mana-dance done", StringComparison.Ordinal));
        Gone(w);
        w.Run(autopilot, a => a.State == AutopilotState.Trip);
        Assert.Contains(arrows, a => a.From == AutopilotState.Fight && a.To == AutopilotState.Trip && a.Why.StartsWith("the keeper's fight-back is over: ", StringComparison.Ordinal));
    }

    /// <summary>A station standing in a wait, then done.</summary>
    private sealed class WaitingRun(string name, string? waiting) : IStationRun, IStationWait
    {
        public string? Waiting => waiting;

        public string Name => name;

        public string Stage => "wait";

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => true;

        public ScenarioStatus Step(IGameSessionView session, double now) => ScenarioStatus.Running;

        public string VerdictFields() => string.Empty;
    }

    [Fact]
    public void A_stations_wait_ends_by_name_on_a_ground_and_the_station_runs_again_from_its_start()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var made = 0;
        w.Factory.Script = spec => spec.Kind != StationKinds.Buff ? null : ++made == 1 ? new WaitingRun(spec.Name, "the buff pass's mana wait") : new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "buffs=24/24");
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Buff && a.CurrentRun is not null);
        Hostile(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Contains(w.Lines, l => l.StartsWith(Stops + " the buff pass's mana wait in the buff station of the buff state", StringComparison.Ordinal));
        Gone(w);
        w.Run(autopilot, a => a.State == AutopilotState.Buff);
        FoughtBackAndResumed(w, arrows, AutopilotState.Buff);
        Assert.Equal(2, made);
    }

    [Fact]
    public void A_station_at_work_is_left_alone()
    {
        var w = new ApWorld(Den, output, atHome: false);
        w.Factory.Script = spec => spec.Kind == StationKinds.Buff ? new WaitingRun(spec.Name, null) : null;
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Buff && a.CurrentRun is not null);
        Hostile(w);
        var until = w.Now + 5;
        w.Run(autopilot, a => w.Now >= until);

        Assert.Equal(AutopilotState.Buff, autopilot.State);
        Assert.DoesNotContain(w.Lines, l => l.StartsWith(Stops, StringComparison.Ordinal));
    }

    [Fact]
    public void A_creature_that_never_attacks_holds_the_state_only_to_the_quiet_cap_and_then_only_a_new_attack_stops_it()
    {
        var w = new ApWorld(Den, output);
        var hold = true;
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? new ScriptedRun(spec.Name, _ => hold ? null : ScenarioStatus.Succeeded, () => "sold=3 bought=2") : null;
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 2 });
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Trip);
        Hostile(w); // asleep behind a wall, say: in reach, and it never comes
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        var from = w.Now;
        w.Run(autopilot, _ => w.Lines.Any(l => l.Contains("autopilot trip: station logistics starts", StringComparison.Ordinal)));

        Assert.InRange(w.Now - from, AutopilotScenario.KeeperFightQuietCapSeconds, AutopilotScenario.KeeperFightQuietCapSeconds + 5);
        Assert.Contains(w.Lines, l => l.Contains("TODO(keeper-fight-awake-read)", StringComparison.Ordinal) && l.Contains("the trip state resumes", StringComparison.Ordinal));
        Assert.Single(arrows, a => a.To == AutopilotState.Fight && a.From == AutopilotState.Trip);
        hold = false;
    }

    [Fact]
    public void An_attacker_on_her_has_no_cap_the_fight_stands_until_she_is_quiet()
    {
        var w = new ApWorld(Den, output);
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? Trip(spec) : null;
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 2 });
        w.Run(autopilot, a => a.State == AutopilotState.Trip);
        Hostile(w);
        Hit(w);
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        var until = w.Now + AutopilotScenario.KeeperFightQuietCapSeconds + 20;
        var next = w.Now;
        w.Run(
            autopilot,
            a => w.Now >= until,
            perFrame: _ =>
            {
                if (w.Now >= next)
                {
                    next = w.Now + 3;
                    Hit(w);
                }
            });

        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.DoesNotContain(w.Lines, l => l.Contains("TODO(keeper-fight-awake-read)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_forced_trip_waits_for_the_lull_by_name_and_goes_once_the_reach_is_clear()
    {
        var w = new ApWorld(Den, output);
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? Trip(spec) : null;
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 2 });
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is not null);
        Hostile(w);
        var until = w.Now + 90; // far past the two kills
        w.Run(autopilot, a => w.Now >= until);

        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Single(w.Lines, l => l.StartsWith("autopilot fight: the trip waits for the lull: the forced trip: Tusker Guard 0x80000BD1 at 6 m", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("stop asked (forced-trip)", StringComparison.Ordinal));

        Gone(w);
        w.Run(autopilot, a => a.State == AutopilotState.Trip);
        Assert.Contains(w.Lines, l => l.Contains("stop asked (forced-trip)", StringComparison.Ordinal));
    }

    [Fact]
    public void The_counters_trip_waits_for_the_lull_by_name()
    {
        var ground = GroundCatalog.Get(Den);
        var low = ground.Supplies.Stock.First(l => !ground.IsCarried(l.Wcid));
        var lowered = false;
        var w = new ApWorld(Den, output, atHome: false, onKill: (world, _) => // the fight uses the line up with a hostile on her
        {
            if (!lowered)
            {
                lowered = true;
                Hostile(world);
                world.SetHeld(low.Wcid, Math.Max(low.StopUnder, low.TripUnder - 1));
            }
        });
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? Trip(spec) : null;
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => lowered);
        var until = w.Now + 60;
        w.Run(autopilot, a => w.Now >= until);

        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Single(w.Lines, l => l.StartsWith("autopilot fight: the trip waits for the lull: ", StringComparison.Ordinal) && l.Contains("Tusker Guard 0x80000BD1 at 6 m", StringComparison.Ordinal));
        Gone(w);
        w.Run(autopilot, a => a.State == AutopilotState.Trip);
        Assert.Contains(w.Lines, l => l.Contains("stop asked (logistics-trip)", StringComparison.Ordinal));
    }

    /// <summary>A level up run on the outskirts whose first kill burns a stock line under its stop line (sold at home, so a trip restocks it) with an attacker on her; every scrivener's line known, so nothing else calls a trip.</summary>
    private ApWorld Climber(bool payable, out AutopilotScenario autopilot, out Func<bool> burned)
    {
        const string Outskirts = "holtburg-outskirts";
        var ground = GroundCatalog.Get(Outskirts);
        var low = ground.Supplies.Stock.First(l => !ground.IsCarried(l.Wcid) && l.StopUnder > 0);
        var done = false;
        var w = new ApWorld(Outskirts, output, atHome: false, onKill: (world, kills) =>
        {
            if (kills >= 2 && !done)
            {
                done = true;
                Hit(world);
                world.SetHeld(low.Wcid, low.StopUnder - 1);
            }
        })
        {
            Spells = new Dictionary<uint, SpellInfo> { [RecallHome.LifestoneRecallSpell] = WorldActionRig.Spell(RecallHome.LifestoneRecallSpell, SpellType.Boost, SpellFlags.SelfTargeted) with { Name = "Lifestone Recall", BaseMana = 0 } },
        };
        w.Level(5);
        w.Quests = new QuestTimers("Keeper");
        w.Quests.Complete(QuestCatalog.Get("worcer-heirlooms"), DateTimeOffset.UnixEpoch, QuestTimers.HowReply); // M6 8 B2a: level 5 is in the first ladder errand's band; recorded done, as this rig has no quest station (the errand is LadderErrandTests' subject)
        var book = w.Trip.Vendor.State.Self.Spellbook;
        book.IsKnown = true;
        foreach (var spell in ScrivenerCatalog.Default.Spells)
        {
            book.Learn(spell.SpellId);
        }

        book.Learn(RecallHome.LifestoneRecallSpell);
        w.Prime = c => c.Facts.LifestoneRecallUnavailable = !payable;
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? Trip(spec) : null;
        autopilot = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, Minutes = 30 });
        burned = () => done;
        return w;
    }

    [Fact]
    public void A_stop_line_with_an_attacker_on_her_takes_lifestone_recall_by_name_with_no_keepers_pass_and_the_trip_runs_from_home()
    {
        var w = Climber(payable: true, out var autopilot, out var burned);
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        var next = 0.0;
        w.Run(
            autopilot,
            a => a.State == AutopilotState.Trip && arrows.Any(t => t.Why.StartsWith("urgent-trip: ", StringComparison.Ordinal)),
            8000,
            _ =>
            {
                if (burned() && autopilot.State == AutopilotState.Fight && w.Now >= next)
                {
                    next = w.Now + 3;
                    Hit(w);
                }
            });

        Assert.Contains(w.Lines, l => l.StartsWith("autopilot fight: urgent-trip: ", StringComparison.Ordinal) && l.Contains("Lifestone Recall is payable", StringComparison.Ordinal));
        Assert.Contains(arrows, a => a.From == AutopilotState.Fight && a.To == AutopilotState.Travel && a.Why.StartsWith("urgent-trip: ", StringComparison.Ordinal));
        Assert.Contains(RecallHomeStation.StationName, Kinds(w));
        Assert.DoesNotContain(w.Lines, l => l.StartsWith(Stops, StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("buff refresh: leave asked", StringComparison.Ordinal));
        Assert.Equal(AutopilotState.Trip, autopilot.State);
        Assert.Contains(arrows, a => a.To == AutopilotState.Trip && a.Why == "the trip again after it was cut");
        Assert.Equal(JourneyPlans.TripForced, w.Factory.Made.Last(s => s.Kind == StationKinds.Logistics).Arguments[JourneyPlans.TripArgument]); // the stop line is what it restocks
    }

    [Fact]
    public void A_stop_line_with_lifestone_recall_not_payable_fights_on_by_name_and_the_trip_waits_for_the_lull()
    {
        var w = Climber(payable: false, out var autopilot, out var burned);
        var next = 0.0;
        var seen = double.NaN;
        w.Run(
            autopilot,
            _ => !double.IsNaN(seen) && w.Now >= seen + 30,
            8000,
            _ =>
            {
                if (burned() && autopilot.State == AutopilotState.Fight && w.Now >= next)
                {
                    next = w.Now + 3;
                    Hit(w);
                }

                if (double.IsNaN(seen) && w.Lines.Any(l => l.StartsWith("autopilot fight: urgent-trip: ", StringComparison.Ordinal)))
                {
                    seen = w.Now;
                }
            });

        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Single(w.Lines, l => l.StartsWith("autopilot fight: urgent-trip: ", StringComparison.Ordinal) && l.Contains("Lifestone Recall is not payable (refused for components this run)", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("stop asked (logistics-trip)", StringComparison.Ordinal));
        w.Run(autopilot, a => a.State == AutopilotState.Trip, 2000); // the attacks stop: the lull
        Assert.Equal(AutopilotState.Trip, autopilot.State);
    }

    [Theory]
    [InlineData(true, false, null, null, true, 60u, 50u, true, "mana 60 of 50 needed, the components held")]
    [InlineData(true, false, true, null, true, 50u, 50u, true, "mana 50 of 50 needed, the components held")]
    [InlineData(false, false, null, null, true, 60u, 50u, false, "not in the book")]
    [InlineData(true, true, null, null, true, 60u, 50u, false, "refused for components this run")]
    [InlineData(true, false, false, null, true, 60u, 50u, false, "the lifestone tie is known false")]
    [InlineData(true, false, null, "1 Prismatic Taper", true, 60u, 50u, false, "the pack is short: 1 Prismatic Taper")]
    [InlineData(true, false, null, null, false, 0u, 50u, false, "the mana is unread")]
    [InlineData(true, false, null, null, true, 49u, 50u, false, "mana 49 under 50")]
    public void The_urgent_recall_is_paid_or_named(bool known, bool unavailable, bool? tied, string? shortOf, bool manaRead, uint mana, uint cost, bool payable, string why) =>
        Assert.Equal((payable, why), AutopilotScenario.UrgentRecallPayable(known, unavailable, tied, shortOf, manaRead, mana, cost));
}
