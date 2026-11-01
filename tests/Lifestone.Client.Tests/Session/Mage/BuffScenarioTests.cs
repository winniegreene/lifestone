using System.Text.RegularExpressions;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The buff scenario (M3 2.1 s1, <see cref="BuffScenario"/>) on <see cref="ScriptedCaster"/>, a scripted ACE that answers casts as the real one does, with the gear already on (a ready mage: the gear scenario inside sends no step):
/// the whole run (mode, cycle with its upgrade pass, the forced pass that recasts one line, non-combat) ends with <c>buffs=24/24 rebuffed=yes forced=yes</c>; a second run casts nothing, on the same cycle or on a new cycle object on the same session (the session's
/// <see cref="ArmourBuffLedger"/>, M4 4.1 lane b; closes <c>TODO(m3-buff-armour-memory)</c>); a failure at any stage logs off before it is reported; the verdict line's field order; and the scenario allocates nothing while it waits.
/// </summary>
public sealed class BuffScenarioTests
{
    private static readonly Regex Verdict = new(
        @"^gear=(wielded|failed) worn=\d buffs=\d+/24 levels=(n/a|(\w+:(I|II|III|IV|V|VI|VII|VIII|-),){23}\w+:(I|II|III|IV|V|VI|VII|VIII|-)) rebuffed=(yes|no) forced=(yes|no) min_remaining_s=(\d+|n/a) transfers=\d+/\d+ revitalizes=\d+ fizzles=\d+ mana_min=(\d+|n/a) cycle_s=\d+ mana_tries=\d+ mana_refusals=\d+ mana_landed=\d+ busy=\d+ resists=\d+ proved=\d+( mana_presses=\d+ dances=\d+ dances_landed=\d+)? misses=\S+ surpassed=\d+$",
        RegexOptions.CultureInvariant);

    /// <summary>The session view over the scripted rig: the real read model, clock, bus and Action API.</summary>
    private sealed class View(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }

    private sealed class Rig
    {
        public Rig(uint mana = 426, uint stamina = 275, BuffOptions? options = null, bool magic = false)
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, mana, stamina);
            Options = options ?? new BuffOptions();
            View = new View(R);
            if (!magic)
            {
                R.CombatModeIs(CombatModes.NonCombat); // the scenario enters the magic mode itself
            }
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public BuffOptions Options { get; }

        public IGameSessionView View { get; }

        public double Now => R.H.R.Clock.Now;

        public GearKit Kit { get; } = GearKit.Of([GearItemOf(GearKit.Wand.Wcid, GearKit.Wand.Name, GearKind.Wand, GearKit.Wand), .. GearKit.Armour.Select(a => GearItemOf(a.Wcid, a.Name, GearKind.Armour, a))]);

        public BuffScenario Scenario(bool endSession = false, BuffCycle? cycle = null, IArmourLedgerStore? store = null) =>
            new(Kit, Caster.Table, Options, R.H.Log, endSession: endSession, cycle: cycle, store: store);

        private static GearItem GearItemOf(uint wcid, string name, GearKind kind, GearWeenie w) =>
            new(wcid, name, kind, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.Burden);

        /// <summary>One frame: the clock advances, the Action API sends, the scripted ACE answers, the Action API completes, then the scenario steps.</summary>
        public ScenarioStatus Frame(BuffScenario scenario, double dt = 0.25)
        {
            R.Tick(dt);
            Caster.Pump();
            R.Tick();
            return scenario.Step(View, Now);
        }

        public ScenarioStatus Run(BuffScenario scenario, double maxSeconds = 6000, double dt = 0.25)
        {
            var end = Now + maxSeconds;
            var status = ScenarioStatus.Running;
            while (status == ScenarioStatus.Running && Now < end)
            {
                status = Frame(scenario, dt);
            }

            return status;
        }

        /// <summary>ACE's answer to a log off: the character list arrives and the session is back at character select.</summary>
        public void AnswerLogOff()
        {
            R.H.Phase(SessionPhase.InWorld, SessionPhase.LoggingOff);
            R.H.R.Reducer.OnCharacterLogOff(R.H.R.Context);
            R.H.CharacterList(("BSD Mage", 0u, ActionHarness.Amy));
            R.H.Phase(SessionPhase.LoggingOff, SessionPhase.CharacterSelect);
        }

        public int Sent(FakeSendKind kind) => R.H.Net.Sends.Count(s => s.Kind == kind);

        /// <summary>The buff-line casts (not the mana lines) the scripted ACE answered so far.</summary>
        public int BuffCasts() => Caster.Answered.Count(a => ArenaProfile.Lines.Any(l => l.IsBuff && l.Spells.Any(x => x.Id == a.Spell)));
    }

    [Fact]
    public void With_RequireCharacter_a_session_in_the_world_as_another_character_fails_at_the_gear_and_sends_nothing_and_as_BSD_Mage_runs()
    {
        var wrong = new Rig();
        wrong.R.H.State.Self.Name = "Lifestone Throwaway";
        var sends = wrong.R.H.Net.Sends.Count;
        var scenario = new BuffScenario(wrong.Kit, wrong.Caster.Table, wrong.Options, wrong.R.H.Log, endSession: false) { RequireCharacter = true };
        Assert.Equal(ScenarioStatus.Failed, wrong.Run(scenario, maxSeconds: 30));
        Assert.Contains("not BSD Mage", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(sends, wrong.R.H.Net.Sends.Count);

        var right = new Rig();
        right.R.H.State.Self.Name = "BSD Mage";
        var ok = new BuffScenario(right.Kit, right.Caster.Table, right.Options, right.R.H.Log, endSession: false) { RequireCharacter = true };
        Assert.True(right.Run(ok) == ScenarioStatus.Succeeded, ok.Failure);
    }

    [Fact]
    public void The_autopilots_own_station_ends_within_a_second_of_its_last_cast_with_no_driver_wait()
    {
        // M5 7 F24 (soak round 3: the station sat idle 30 s at the lifestone waiting for the re-buff driver's first check): unattended, the ordered cycle's end is the station's end.
        var t = new Rig(options: new BuffOptions { Auto = true, DriverUnattended = true });
        var scenario = new BuffScenario(t.Kit, t.Caster.Table, t.Options, t.R.H.Log, endSession: false);
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 40000 && status == ScenarioStatus.Running; i++)
        {
            status = t.Frame(scenario, 0.25);
        }

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        var lastCast = t.Caster.Answered.Max(x => x.At);
        Assert.InRange(t.Now - lastCast, 0, 2.5); // a frame or two: no 30 s idle
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("no driver wait, the station ends now (F24)", StringComparison.Ordinal));
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("the re-buff driver next", StringComparison.Ordinal));
        Assert.Equal([CombatModes.Magic], t.Caster.ModesSent); // M5 7 F25 (D93): the autopilot's own station leaves the magic mode up for its next station
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("the stance holds: the magic mode stays up for the autopilot's next station (F25)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_persons_buff_returns_to_peace_at_its_end_and_only_the_autopilots_own_station_holds_the_magic_mode()
    {
        // M5 7 F25 (D93): the stance holds only for the autopilot's own station (DriverUnattended); a person's buff (or the login check's) ends in peace as before
        var t = new Rig(options: new BuffOptions { Auto = false });
        var scenario = new BuffScenario(t.Kit, t.Caster.Table, t.Options, t.R.H.Log, endSession: false);
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 40000 && status == ScenarioStatus.Running; i++)
        {
            status = t.Frame(scenario, 0.25);
        }

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(CombatModes.NonCombat, t.Caster.ModesSent[^1]);
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("the stance holds", StringComparison.Ordinal));
    }

    [Fact]
    public void With_the_driver_on_there_is_no_forced_pass_and_the_drivers_own_pass_recasts_from_the_margin_of_the_shortest_line()
    {
        var t = new Rig(options: new BuffOptions { Auto = true });
        var notices = new List<string>();
        var scenario = new BuffScenario(t.Kit, t.Caster.Table, t.Options, t.R.H.Log, endSession: false) { Notice = notices.Add };
        var status = t.Run(scenario);
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Null(scenario.Failure);

        Assert.True(scenario.DriverOn);
        Assert.Null(scenario.ForcedResult); // no forced pass
        var pass = scenario.DriverResult!;
        Assert.False(pass.Forced);
        Assert.True(pass.Rebuffed);
        Assert.Equal(24, pass.Active);
        Assert.True(pass.Cast >= 1);
        Assert.Equal(pass.Cast, scenario.DriverLines);
        Assert.InRange(scenario.DriverMarginSeconds, BuffScenario.DriverMinMarginSeconds, double.MaxValue);
        Assert.InRange(scenario.DriverRebuffSeconds, BuffScenario.DriverLeadSeconds - 5, 300); // about 30 s after the cycle, plus the pass

        var verdict = scenario.VerdictFields();
        Assert.Contains(" rebuffed=yes forced=no ", verdict, StringComparison.Ordinal);
        Assert.Matches(@" driver=on rebuff_lines=[1-9]\d* rebuff_s=\d+$", verdict);
        Assert.Equal([CombatModes.Magic, CombatModes.NonCombat], t.Caster.ModesSent); // the driver found the mage in the magic mode and changed nothing

        Assert.Equal(2, notices.Count);
        Assert.StartsWith("[Lifestone] re-buffing ", notices[0], StringComparison.Ordinal);
        Assert.Matches(@"^\[Lifestone\] re-buffed [1-9]\d* lines?$", notices[1]);
    }

    [Fact]
    public void The_whole_scenario_ends_with_every_buff_active_a_forced_recast_and_the_mage_back_in_non_combat()
    {
        var t = new Rig();
        var scenario = t.Scenario();
        Assert.Equal(ScenarioStatus.Succeeded, t.Run(scenario));
        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);

        var verdict = scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.StartsWith("gear=wielded worn=5 buffs=24/24 levels=Focus:VII,", verdict, StringComparison.Ordinal);
        Assert.Contains(" rebuffed=yes forced=yes ", verdict, StringComparison.Ordinal);
        Assert.EndsWith(" misses=none surpassed=0", verdict, StringComparison.Ordinal);
        Assert.Contains(" cycle_s=", verdict, StringComparison.Ordinal);
        Assert.Contains(" " + BuffLegVerdict.CastFieldsOf(scenario.CycleResult, scenario.ForcedResult) + " misses=none", verdict, StringComparison.Ordinal); // M5 1 fix B (G1): the cast policy's fields, both passes summed, report-only
        Assert.Equal(0, scenario.Gear.GearSteps); // a ready mage: the gear stage sent nothing
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("verdict gear=wielded", StringComparison.Ordinal));

        // The ordered pass cast the 24 lines and the two upgrades; the forced pass recast exactly one line while it was still active.
        Assert.Equal(26, scenario.CycleCast);
        Assert.Equal(2, scenario.CycleArmourCast);
        var forced = scenario.ForcedResult!;
        Assert.True(forced.Forced);
        Assert.Equal(1, forced.Cast);
        Assert.True(forced.Rebuffed);
        Assert.Equal(24, forced.Active);

        // The modes: Magic before the cycle, NonCombat after the forced pass, and no log off or quit (endSession: false).
        Assert.Equal([CombatModes.Magic, CombatModes.NonCombat], t.Caster.ModesSent);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        Assert.Equal(0, t.Sent(FakeSendKind.LogOff));
        Assert.Equal(0, t.Sent(FakeSendKind.Quit));
    }

    [Fact]
    public void A_second_run_casts_nothing_on_the_same_cycle_or_on_a_new_one_on_the_same_session()
    {
        var t = new Rig();
        var first = t.Scenario();
        Assert.Equal(ScenarioStatus.Succeeded, t.Run(first));
        Assert.Equal(26, first.CycleCast);
        var answered = t.BuffCasts();

        // The same cycle object (the game's buff leg keeps it): the ordered pass casts nothing; only the forced pass casts, one line.
        var second = t.Scenario(cycle: first.Cycle);
        Assert.Equal(ScenarioStatus.Succeeded, t.Run(second));
        Assert.Equal(0, second.CycleCast);
        Assert.Equal(1, second.ForcedResult!.Cast);
        Assert.Equal(answered + 1, t.BuffCasts());
        Assert.Contains(" buffs=24/24 ", second.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(" forced=yes ", second.VerdictFields(), StringComparison.Ordinal);
        Assert.Matches(Verdict, second.VerdictFields());

        // A new cycle object on the same session knows the earlier object's armour casts (M4 4.1 lane b, closes TODO(m3-buff-armour-memory)): the scenario hands every cycle it makes the session's ledger, so it casts
        // neither a self line nor an armour line; only the forced pass casts, one line.
        answered = t.BuffCasts();
        var third = t.Scenario();
        Assert.Equal(ScenarioStatus.Succeeded, t.Run(third));
        Assert.NotSame(first.Cycle, third.Cycle);
        Assert.Same(first.Cycle!.Ledger, third.Cycle!.Ledger);
        Assert.Equal(0, third.CycleCast);
        Assert.Equal(0, third.CycleArmourCast);
        Assert.Equal(answered + 1, t.BuffCasts()); // the forced pass's one
        Assert.Contains(" buffs=24/24 ", third.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_scenario_keeps_the_ledgers_remaining_seconds_in_the_store_when_the_run_ends()
    {
        var t = new Rig();
        var store = new MemoryLedgerStore();
        var scenario = t.Scenario(store: store);
        Assert.Equal(ScenarioStatus.Succeeded, t.Run(scenario));
        var guid = t.R.H.State.Self.Guid;
        var saved = ArmourBuffLedger.ReadSnapshot(store.Saved.GetValueOrDefault(guid), guid, out var why);
        Assert.Null(why);
        Assert.Equal(ScriptedCaster.PieceNames.Length * 2, saved.Count); // both armour lines on the five pieces, each with seconds left
        Assert.All(saved, e => Assert.True(e.Remaining > 0));
        Assert.Equal(scenario.Cycle!.Ledger.Count, saved.Count);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("the ledger's remaining seconds are kept for the next login", StringComparison.Ordinal));

        // No store: nothing is kept and nothing fails (the conservative rule: nothing saved, nothing to restore).
        var plain = new Rig();
        Assert.Equal(ScenarioStatus.Succeeded, plain.Run(plain.Scenario()));
    }

    private sealed class MemoryLedgerStore : IArmourLedgerStore
    {
        public Dictionary<uint, string> Saved { get; } = [];

        public string? Load(uint characterGuid) => Saved.GetValueOrDefault(characterGuid);

        public void Save(uint characterGuid, string json) => Saved[characterGuid] = json;
    }

    [Fact]
    public void A_failure_in_the_cycle_logs_off_before_it_is_reported_and_the_verdict_says_what_was_done()
    {
        // Mana 5 and no regeneration: the cycle's mana wait runs out (30 s) and fails the pass.
        var t = new Rig(mana: 5, options: new BuffOptions { ManaWaitSeconds = 30, Mana = Lifestone.Client.Session.Vitals.ManaOrderOptions.Ceiling }); // M5 1 K5: the ceiling order's wait (--cast-mana-tries 0)
        t.Caster.ManaRegenPerSecond = 0;
        var scenario = t.Scenario(endSession: true);
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && scenario.Failure is null; i++)
        {
            status = t.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Contains("cycle: the mana wait ran out", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(ScenarioStatus.Running, t.Frame(scenario));
        Assert.Equal(1, t.Sent(FakeSendKind.LogOff)); // the log off went out before the failure is reported
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("logging off before the run ends as failed", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("verdict gear=wielded worn=5 buffs=0/24", StringComparison.Ordinal));
        Assert.Matches(Verdict, scenario.VerdictFields());
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(ScenarioStatus.Running, t.Frame(scenario)); // still waiting for ACE's answer
        }

        t.AnswerLogOff();
        for (var i = 0; i < 8 && status == ScenarioStatus.Running; i++)
        {
            status = t.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("the failed run logged off", StringComparison.Ordinal));
        Assert.Equal(0, t.Sent(FakeSendKind.Quit));
    }

    [Fact]
    public void A_failure_in_the_gear_stage_logs_off_too_and_the_cycle_never_starts()
    {
        // The kit wants 20 Lead Scarabs the mage does not hold: the scripted server answers no grant, so the gear stage fails on its own limit (or a refusal) and the scenario logs off.
        var t = new Rig();
        var scarab = new GearItem(691, "Lead Scarab", GearKind.Component, GearKit.WeenieTypeSpellComponent, GearKit.ItemTypeSpellComponents, EquipMask.None, 20, 100, GearKit.ComponentBurden);
        var kit = GearKit.Of([.. t.Kit.Items, scarab]);
        var scenario = new BuffScenario(kit, t.Caster.Table, t.Options, t.R.H.Log, endSession: true);
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && scenario.Failure is null; i++)
        {
            status = t.Frame(scenario);
        }

        Assert.StartsWith("gear: ", scenario.Failure, StringComparison.Ordinal);
        Assert.Null(scenario.Cycle); // the cycle never started
        Assert.Equal(ScenarioStatus.Running, t.Frame(scenario));
        Assert.Equal(1, t.Sent(FakeSendKind.LogOff));
        t.AnswerLogOff();
        for (var i = 0; i < 8 && status == ScenarioStatus.Running; i++)
        {
            status = t.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Matches(Verdict, scenario.VerdictFields());
        Assert.Contains(" gear=failed ", " " + scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_noGrant_the_gear_stage_sends_no_item_for_a_stock_line_it_is_short_of_and_the_run_still_buffs_while_the_default_stage_asks_the_mod_M5_3_F2()
    {
        // M5 3 F2 (closes 3-R3, DoD 6): the mage journey's buff station runs this gear stage after play has burned components; it must verify, never top up with the item verb.
        var scarab = new GearItem(691, "Lead Scarab", GearKind.Component, GearKit.WeenieTypeSpellComponent, GearKit.ItemTypeSpellComponents, EquipMask.None, 20, 100, GearKit.ComponentBurden);

        var granting = new Rig();
        var asks = new BuffScenario(GearKit.Of([.. granting.Kit.Items, scarab]), granting.Caster.Table, granting.Options, granting.R.H.Log, endSession: false);
        for (var i = 0; i < 40 && asks.Failure is null; i++)
        {
            granting.Frame(asks);
        }

        Assert.True(granting.R.Api.Tools.Verbs.Counts.Item > 0, "the default gear stage asks the mod for the missing Lead Scarabs (the contrast of this test)");

        var t = new Rig();
        var scenario = new BuffScenario(GearKit.Of([.. t.Kit.Items, scarab]), t.Caster.Table, t.Options, t.R.H.Log, endSession: false, noGrant: true);
        var status = t.Run(scenario);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(0, t.R.Api.Tools.Verbs.Counts.Total); // no mod verb of any kind
        Assert.Equal(0, t.R.H.Net.ToolRequests().Count(r => r is not null && r.StartsWith("@lifestone item ", StringComparison.Ordinal)));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("Lead Scarab", StringComparison.Ordinal) && l.Contains("is held 0 of 20 and play grants nothing", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("kit verified in the pack (no grant", StringComparison.Ordinal));
        Assert.Matches(Verdict, scenario.VerdictFields());
    }

    [Fact]
    public void A_missed_line_fails_the_run_and_a_run_without_a_session_to_end_reports_the_failure_at_once()
    {
        var t = new Rig();
        t.Caster.SilencePiece("Leather Boots"); // ACE names no piece the armour lines enchanted: both armour lines are misses
        var scenario = t.Scenario();
        Assert.Equal(ScenarioStatus.Failed, t.Run(scenario));
        Assert.Contains("lines were missed (Impen:no-piece:Leather_Boots;BludB:no-piece:Leather_Boots)", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(" buffs=22/24 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(" misses=Impen:no-piece:Leather_Boots;BludB:no-piece:Leather_Boots", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Matches(Verdict, scenario.VerdictFields());
        Assert.Equal(0, t.Sent(FakeSendKind.LogOff)); // endSession: false leaves the session to its owner
    }

    [Fact]
    public void A_successful_run_that_ends_the_session_logs_off_and_quits()
    {
        var t = new Rig();
        var scenario = t.Scenario(endSession: true);
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 100000 && status == ScenarioStatus.Running && t.Sent(FakeSendKind.LogOff) == 0; i++)
        {
            status = t.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal("logging-off", scenario.Stage);
        Assert.Equal([CombatModes.Magic, CombatModes.NonCombat], t.Caster.ModesSent); // non-combat before the log off
        t.AnswerLogOff();
        for (var i = 0; i < 8 && status == ScenarioStatus.Running; i++)
        {
            status = t.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1, t.Sent(FakeSendKind.Quit));
        Assert.Null(scenario.Failure);
    }

    [Fact]
    public void The_scenario_allocates_nothing_while_the_cycle_waits_for_mana()
    {
        var t = new Rig(mana: 5, options: new BuffOptions { Mana = Lifestone.Client.Session.Vitals.ManaOrderOptions.Ceiling }); // M5 1 K5: the ceiling order's wait (--cast-mana-tries 0)
        t.Caster.ManaRegenPerSecond = 0;
        var scenario = t.Scenario();
        for (var i = 0; i < 400 && scenario.Cycle?.Stage != BuffCycle.StageManaWait; i++)
        {
            Assert.Equal(ScenarioStatus.Running, t.Frame(scenario));
        }

        Assert.Equal("cycle", scenario.Stage);
        Assert.Equal(BuffCycle.StageManaWait, scenario.Cycle!.Stage);
        scenario.Step(t.View, t.Now);
        long allocated = 0;
        for (var i = 0; i < 300; i++)
        {
            t.R.H.R.Time.Advance(TimeSpan.FromSeconds(0.01)); // 3 s of the session clock in all: a few of the wait's own looks, short of its NonCombat threshold
            var before = GC.GetAllocatedBytesForCurrentThread();
            scenario.Step(t.View, t.Now);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(0, allocated);
        Assert.Equal("cycle", scenario.Stage);
    }
}
