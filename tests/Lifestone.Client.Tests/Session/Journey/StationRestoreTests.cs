using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Net.Types;
using Xunit;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 precond (live 2026-11-17 06:29:00, <c>cache/m6/14/live/ladder54/step3-levelup.log</c>: "station quest-atlan-wand-hunt-4: precondition health does not hold at its first step: ended without running (why=precondition:health)"
/// at 170/219 health and 403/410 mana): check, act, check. A station whose vital need she can restore herself has the journey's keeper restore it first (Heal Self here), logged before and after, and then runs; a need she cannot
/// meet by her own means ends the station by name. The station is a <see cref="ScenarioStation"/> with the den-fight's own needs (health <see cref="JourneyPlans.FightHealthPercent"/>, mana 50%), over the keeper's scripted rig.
/// </summary>
public sealed class StationRestoreTests
{
    private static readonly uint[] HealSelfToIII = [6, 1157, 1158];

    /// <summary>The den-fight station's needs (JourneyStations.DenFight), here rather than in the den: the place is not what this test reads.</summary>
    private static StationNeeds DenFightNeeds => new(StationWhere.Here, MinHealthPercent: JourneyPlans.FightHealthPercent, MinManaPercent: 50);

    private sealed class Probe : IScenario
    {
        public int Steps { get; private set; }

        public string Name => "probe";

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public string Stage => "probe";

        public string? Failure => null;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            Steps++;
            return ScenarioStatus.Succeeded;
        }
    }

    private static (C2VitalsRig Rig, RecordingLogSink Log, IStationRun Run, Probe Probe) Make(C2Character character, bool keeper = true, bool fight = false)
    {
        var rig = new C2VitalsRig(character);
        var log = new RecordingLogSink();
        var probe = new Probe();
        var station = new ScenarioStation("quest-atlan-wand-hunt-4", DenFightNeeds, _ => probe, _ => "probe=ran", (_, status) => status == ScenarioStatus.Succeeded, fightGuard: fight ? state => JourneyStations.EmergencyGuard(state, new FightOptions()) : null);
        var context = new JourneyContext { Character = "Qwinnie", Account = "test", Log = log, Travel = new PlaceTravel(), Spells = rig.Table, Keeper = keeper ? rig.Keeper : null };
        return (rig, log, station.Start(context), probe);
    }

    private static ScenarioStatus RunToEnd(C2VitalsRig rig, IStationRun run, double maxSeconds = 600)
    {
        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < maxSeconds && status == ScenarioStatus.Running; t += 0.25)
        {
            rig.R.Tick(0.25);
            rig.Pump();
            rig.R.Tick();
            status = run.Step(rig.Session, rig.Now);
        }

        return status;
    }

    [Fact]
    public void A_den_fight_station_met_at_170_of_219_health_heals_first_with_its_checks_logged_and_then_runs()
    {
        var (rig, log, run, probe) = Make(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 170, MaxHealth = 219, Mana = 190 });

        var status = RunToEnd(rig, run);

        Assert.Equal(ScenarioStatus.Succeeded, status); // live: Failed at the first step, precondition:health
        Assert.True(run.Ok);
        Assert.Equal(1, probe.Steps);
        Assert.Contains("cast:1158", rig.Used); // Heal Self, her own means
        Assert.True(rig.Health * 100.0 / 219 >= JourneyPlans.FightHealthPercent);
        Assert.Contains(log.Entries, e => e.Message.StartsWith("station quest-atlan-wand-hunt-4: precondition health: check before: health 170/219, ", StringComparison.Ordinal) && e.Message.Contains("act: the keeper's pass restores it, then the station runs", StringComparison.Ordinal));
        Assert.Contains(log.Entries, e => e.Message.StartsWith("station quest-atlan-wand-hunt-4: precondition health: check after: health ", StringComparison.Ordinal) && e.Message.EndsWith("the keeper's pass is done, the needs are read again", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("does not hold at its first step", StringComparison.Ordinal));
        Assert.Equal("probe=ran", run.VerdictFields());
    }

    /// <summary>A Nubilous Golem <paramref name="metres"/> east of her, in her cell.</summary>
    private static void GolemAt(C2VitalsRig rig, float metres)
    {
        var here = rig.Session.State.Self.Position!.Value;
        var basis = ReducerHarness.Create(0x80006001, ReducerHarness.Stamps(), ReducerHarness.Position(here.CellId, here.Frame.Origin.X + metres, here.Frame.Origin.Y, here.Frame.Origin.Z), name: "Nubilous Golem");
        rig.R.H.R.Reducer.OnCreateObject(rig.R.H.R.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, ObjectDescriptionFlags = 0x10, WeenieClassId = 23082 } }, isUpdate: false);
    }

    [Fact]
    public void A_den_fight_station_at_170_of_219_with_a_hostile_5_m_off_starts_the_fight_over_its_emergency_line_and_ends_by_name_under_it()
    {
        // The coordinator's amendment: the fight's own guard heals in combat, so a fight station does not sit down to heal among them and does not end; the line is the fight's at its start (floor 60%, seed 2.3 a second x 1 s x 3 + margin 10%).
        var (rig, log, run, probe) = Make(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 170, MaxHealth = 219, Mana = 190 }, fight: true);
        GolemAt(rig, 5);
        var line = JourneyStations.EmergencyGuard(rig.Session.State, new FightOptions())!.Value.LineHealth;
        Assert.InRange(line, 100, 169); // 60% of 219 = 131.4

        Assert.Equal(ScenarioStatus.Succeeded, RunToEnd(rig, run));
        Assert.Equal(1, probe.Steps); // the fight ran
        Assert.DoesNotContain(rig.Used, u => u.StartsWith("cast:", StringComparison.Ordinal)); // no keeper's pass among them
        Assert.Contains(log.Entries, e => e.Message.StartsWith("station quest-atlan-wand-hunt-4: precondition health: check before: health 170/219 under the station's 80% with 1 hostile(s) within 24 m (the nearest at 5 m): the fight starts; its guard heals in combat, the emergency line is 131 (60%)", StringComparison.Ordinal));

        // Under the line the station ends by name; the survival layer above it decides.
        var (low, lowLog, lowRun, lowProbe) = Make(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 120, MaxHealth = 219, Mana = 190 }, fight: true);
        GolemAt(low, 5);
        Assert.Equal(ScenarioStatus.Failed, RunToEnd(low, lowRun));
        Assert.Equal(0, lowProbe.Steps);
        Assert.Equal("precondition:health:under-attack", lowRun.Failure);
        Assert.Contains(lowLog.Entries, e => e.Message.EndsWith(", at or under the emergency line 131: the fight does not start; the survival layer decides", StringComparison.Ordinal));
    }

    [Fact]
    public void A_station_she_cannot_heal_for_ends_by_name_with_a_hostile_in_reach_or_with_no_keeper()
    {
        // A hostile 5 m off: she does not sit down to heal among them (the survival layer's), so the station ends by name with nothing run.
        var (rig, log, run, probe) = Make(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 170, MaxHealth = 219, Mana = 190 });
        GolemAt(rig, 5); // a station that is not a fight: no fight guard

        Assert.Equal(ScenarioStatus.Failed, RunToEnd(rig, run));
        Assert.Equal(0, probe.Steps);
        Assert.Equal("precondition:health:under-attack", run.Failure);
        Assert.Equal("why=precondition:health:under-attack", run.VerdictFields());
        Assert.DoesNotContain(rig.Used, u => u.StartsWith("cast:", StringComparison.Ordinal));
        Assert.Contains(log.Entries, e => e.Message.Contains("1 hostile(s) within 24 m (the awake reach): no keeper's pass among them", StringComparison.Ordinal));

        // No keeper in the journey: nothing restores it, named.
        var (bare, bareLog, bareRun, bareProbe) = Make(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 170, MaxHealth = 219, Mana = 190 }, keeper: false);
        Assert.Equal(ScenarioStatus.Failed, RunToEnd(bare, bareRun));
        Assert.Equal(0, bareProbe.Steps);
        Assert.Equal("precondition:health:no-keeper", bareRun.Failure);
        Assert.Contains(bareLog.Entries, e => e.Message == "station quest-atlan-wand-hunt-4: precondition health:no-keeper does not hold at its first step: ended without running (why=precondition:health:no-keeper)");
    }
}
