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
/// the whole run (mode, cycle with its upgrade pass, the forced pass that recasts one line, non-combat) ends with <c>buffs=24/24 rebuffed=yes forced=yes</c>; a second run on the same cycle casts nothing and a new cycle object recasts
/// only the two armour lines (<c>TODO(m3-buff-armour-memory)</c>); a failure at any stage logs off before it is reported; the verdict line's field order; and the scenario allocates nothing while it waits.
/// </summary>
public sealed class BuffScenarioTests
{
    private static readonly Regex Verdict = new(
        @"^gear=(wielded|failed) worn=\d buffs=\d+/24 levels=(n/a|(\w+:(I|II|III|IV|V|VI|VII|VIII|-),){23}\w+:(I|II|III|IV|V|VI|VII|VIII|-)) rebuffed=(yes|no) forced=(yes|no) min_remaining_s=(\d+|n/a) transfers=\d+/\d+ revitalizes=\d+ fizzles=\d+ mana_min=(\d+|n/a) cycle_s=\d+ misses=\S+$",
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

        public BuffScenario Scenario(bool endSession = false, BuffCycle? cycle = null) =>
            new(Kit, Caster.Table, Options, R.H.Log, endSession: endSession, cycle: cycle);

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
        Assert.EndsWith(" misses=none", verdict, StringComparison.Ordinal);
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
    public void A_second_run_casts_nothing_on_the_same_cycle_and_only_the_armour_lines_on_a_new_one()
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

        // A new cycle object does not know an earlier object's armour casts (TODO(m3-buff-armour-memory)): it recasts the two armour lines, which the registry cannot show, and no self line.
        answered = t.BuffCasts();
        var third = t.Scenario();
        Assert.Equal(ScenarioStatus.Succeeded, t.Run(third));
        Assert.Equal(2, third.CycleCast);
        Assert.Equal(2, third.CycleArmourCast);
        Assert.Equal(answered + 3, t.BuffCasts()); // the two armour lines and the forced pass's one
        Assert.Contains(" buffs=24/24 ", third.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_in_the_cycle_logs_off_before_it_is_reported_and_the_verdict_says_what_was_done()
    {
        // Mana 5 and no regeneration: the cycle's mana wait runs out (30 s) and fails the pass.
        var t = new Rig(mana: 5, options: new BuffOptions { ManaWaitSeconds = 30 });
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
        var t = new Rig(mana: 5);
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
