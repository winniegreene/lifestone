using System.Numerics;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AA (r3c, level 10: Flame Bolt II resisted at 2.6 m, 3 s of hit wait with no decision while two Female Tuskers took 22 health to 5, then a drain on a full tusker, dead): the hit wait never blinds the fight, a resist ends it, it is the flight plus a measured margin,
/// and when the next hit kills a resistible drain does not beat a sure heal. Over the scripted caster; nothing here needs a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const double OneBoltFlight = 1.0; // the rig's tusker at y = 40, 20 m from the mage, its bolt at 20 m/s

    /// <summary>From now on a bolt flies with no answer: the projectile exists, no damage line, resist or kill follows until the test sends one.</summary>
    private static void BoltsFlySilently(FightManaTests.Rig r)
    {
        var next = 0x8000A000u;
        r.Caster.ProjectileHook = (_, _) =>
            r.R.H.R.Reducer.OnCreateObject(
                r.R.H.R.Context,
                ReducerHarness.Create(next++, ReducerHarness.Stamps(), ReducerHarness.Position(0x01050303, PlayerX, PlayerY, 30), state: PhysicsState.Missile | PhysicsState.Gravity, velocity: new Vector3(0, 20, 0)),
                isUpdate: false);
    }

    /// <summary>Runs until one more bolt is confirmed: the fight is in its hit wait.</summary>
    private static void UntilInTheWait(FightManaTests.Rig r, FightScenario scenario)
    {
        var before = Confirmed(r);
        r.Run(scenario, 15, 0.05, () => Confirmed(r) > before);
        Assert.True(Confirmed(r) > before, "a bolt was confirmed");
    }

    private static int Confirmed(FightManaTests.Rig r) => r.Log.Lines.Count(l => l.Contains("cast confirmed: projectile", StringComparison.Ordinal));

    private static bool Logged(FightManaTests.Rig r, string text) => r.Log.Lines.Any(l => l.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void A_hit_taken_in_the_hit_wait_ends_it_under_the_line_and_the_emergency_acts_at_once_M6_7_AA()
    {
        var (r, scenario, spells, t) = Engaged();
        var max = MaxHealthOf(r);
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.9f);
        BoltsFlySilently(r);
        UntilInTheWait(r, scenario);
        var misses = scenario.Tally.Misses;
        var from = spells.Count;

        r.Caster.SetHealth(max * 40 / 100);
        Hit(r, scenario, 3, 0.1);

        Assert.True(Logged(r, "hit wait: ended "), "the wait ended");
        Assert.True(Logged(r, "the emergency acts now; the bolt stays counted in flight, no miss"));
        var first = FirstAfter(r, scenario, spells, from);
        Assert.False(FightManaTests.Rig.IsBolt(first), $"the emergency's heal, not bolt {first}");
        Assert.Equal(misses, scenario.Tally.Misses); // the bolt is not judged
    }

    [Fact]
    public void Entering_the_hit_wait_already_under_the_line_ends_it_on_the_first_frame_the_blow_fell_in_the_wind_up_M6_7_AA()
    {
        var (r, scenario, _, t) = Engaged();
        var max = MaxHealthOf(r);
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.9f);
        BoltsFlySilently(r);
        var fly = r.Caster.ProjectileHook!;
        r.Caster.ProjectileHook = (spell, target) =>
        {
            r.Caster.SetHealth(max * 40 / 100); // r3c: the blow at 51.9 fell in the bolt's wind-up, confirmed at 52.97
            fly(spell, target);
        };
        UntilInTheWait(r, scenario);
        r.Run(scenario, 0.2, 0.05);

        Assert.True(Logged(r, "hit wait: ended 0.0"), "it ended within a frame or two of the confirmation");
    }

    [Fact]
    public void Over_the_line_a_hit_leaves_the_wait_running_M6_7_AA()
    {
        var (r, scenario, _, t) = Engaged();
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.9f);
        BoltsFlySilently(r);
        UntilInTheWait(r, scenario);

        Hit(r, scenario, 1, 0.1);

        Assert.False(Logged(r, "hit wait: ended "));
    }

    [Fact]
    public void A_finishing_bolt_in_flight_holds_the_wait_with_no_second_cast_and_the_kill_ends_it_M6_7_AA()
    {
        var (r, scenario, spells, t) = Engaged();
        var max = MaxHealthOf(r);
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.02f); // well under the bolt in flight
        BoltsFlySilently(r);
        UntilInTheWait(r, scenario);
        var from = spells.Count;

        r.Caster.SetHealth(max * 40 / 100);
        Hit(r, scenario, 3, 0.3); // 0.3 s into a 1 s flight

        Assert.True(Logged(r, "hit wait: held at health "), "the wait held for the bolt");
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("hit wait: ended ", StringComparison.Ordinal));
        Assert.Equal(from, spells.Count); // nothing cast at a tusker a bolt is about to kill
        Assert.True(scenario.FinishingBolt, "the retreat rule waits for it");
    }

    [Fact]
    public void A_resisted_finishing_bolt_ends_the_hold_and_the_emergency_acts_M6_7_AA()
    {
        var (r, scenario, spells, t) = Engaged();
        var max = MaxHealthOf(r);
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.02f);
        BoltsFlySilently(r);
        UntilInTheWait(r, scenario);
        r.Caster.SetHealth(max * 40 / 100);
        Hit(r, scenario, 3, 0.2);
        Assert.True(Logged(r, "hit wait: held at health "));
        var from = spells.Count;

        r.R.Raw(AceWireWriters.ServerMessage($"{Slave} resists your spell", 7));
        var first = FirstAfter(r, scenario, spells, from);

        Assert.True(Logged(r, "miss: the bolt was resisted"));
        Assert.True(FightManaTests.Rig.IsBolt(first), $"the ledger holds no resisted bolt: the finishing bolt is cast again, not spell {first}"); // Y's rule: the tusker is one bolt from dead and she lives through the cast
        Assert.False(scenario.FinishingBolt && spells.Count == from);
    }

    [Fact]
    public void A_finishing_bolt_in_flight_does_not_hold_when_the_next_blow_comes_first_M6_7_AA()
    {
        var (r, scenario, _, t) = Engaged();
        var max = MaxHealthOf(r);
        var health = Math.Max(4, max / 20);
        Hit(r, scenario, health * 2);
        TuskerHealth(r, t[0], 0.02f);
        BoltsFlySilently(r);
        UntilInTheWait(r, scenario);

        Hit(r, scenario, health * 2, 0);
        r.Caster.SetHealth(health); // the blow above read, not lived through: the bolt is a second away, a blow is due sooner
        r.Run(scenario, 0.2, 0.05);

        Assert.True(Logged(r, "hit wait: ended "), "the kill is too far off: the wait ends");
        Assert.False(Logged(r, "hit wait: held at health "));
    }

    [Fact]
    public void A_resist_line_ends_the_wait_at_once_as_a_miss_and_the_next_bolt_follows_M6_7_AA()
    {
        var (r, scenario, _, t) = Engaged();
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.9f);
        BoltsFlySilently(r);
        UntilInTheWait(r, scenario);
        var misses = scenario.Tally.Misses;
        var confirmed = Confirmed(r);
        var at = r.Now;

        r.R.Raw(AceWireWriters.ServerMessage($"{Slave} resists your spell", 7));
        r.Run(scenario, 0.2, 0.05);

        Assert.Equal(misses + 1, scenario.Tally.Misses);
        Assert.True(Logged(r, "miss: the bolt was resisted"));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("no damage line or kill came within", StringComparison.Ordinal));
        Assert.True(r.Now - at < OneBoltFlight, "well inside the wait");
        UntilInTheWait(r, scenario);
        Assert.True(Confirmed(r) > confirmed);
    }

    [Theory]
    [InlineData("You resist the spell cast by Tusker Slave")] // a creature's spell at the mage says nothing of the bolt
    [InlineData("Drudge Sneaker resists your spell")] // nor another creature's resist of a ring
    public void A_resist_that_is_not_the_bolts_answer_leaves_the_wait_running_M6_7_AA(string line)
    {
        var (r, scenario, _, t) = Engaged();
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.9f);
        BoltsFlySilently(r);
        UntilInTheWait(r, scenario);
        var misses = scenario.Tally.Misses;

        r.R.Raw(AceWireWriters.ServerMessage(line, 7));
        r.Run(scenario, 0.2, 0.05);

        Assert.Equal(misses, scenario.Tally.Misses);
        Assert.False(Logged(r, "miss: the bolt was resisted"));
    }

    [Fact]
    public void A_bolt_with_no_answer_is_a_miss_at_its_flight_plus_the_margin_not_at_three_seconds_M6_7_AA()
    {
        var (r, scenario, _, t) = Engaged();
        Hit(r, scenario, 3);
        TuskerHealth(r, t[0], 0.9f);
        BoltsFlySilently(r);
        UntilInTheWait(r, scenario);
        var at = r.Now;
        var misses = scenario.Tally.Misses;

        r.Run(scenario, 3, 0.05, () => scenario.Tally.Misses > misses);

        Assert.InRange(r.Now - at, OneBoltFlight + FightScenario.HitWaitMarginSeconds - 0.2, OneBoltFlight + FightScenario.HitWaitMarginSeconds + 0.2);
        Assert.True(Logged(r, "no damage line or kill came within 1.7 s"));
    }

    private static readonly HealCandidate HealSelfI = new("Heal Self I", 0, 17.5, 0, 0.97, 10, 1.6);

    private static readonly IncomingRead R3cRead = new(10.2, 9, 0.8, 1.0, 3, true); // the log at 09:26:56: 5 of 44, a hit every 0.8 s, the last 1.0 s ago

    [Theory]
    [InlineData(1.6)] // the seed: the two casts level, the sure one wins
    [InlineData(2.5)] // the wind-up the log measured
    public void The_r3c_emergency_heals_when_the_next_hit_kills_the_resistible_drain_does_not_beat_the_sure_heal_M6_7_AA(double drainSeconds)
    {
        var drain = new HealCandidate("Drain Health Other I", 0x800020CB, 39, 78, 0.8, 10, drainSeconds);

        Assert.True(FightHealPick.HealSelfSavesSurer(in HealSelfI, in drain, in R3cRead, 5, out var self, out var other));
        Assert.True(self > other, $"{self} over {other}");
    }

    [Fact]
    public void The_r3c_emergency_at_22_health_heals_too_the_drains_cast_outlasts_her_though_the_next_hit_does_not_kill_M6_7_AA()
    {
        var read = R3cRead with { BiggestHit = 8 }; // the confirmation at 52.97: 22 of 44, two blows to go before the third kills
        var drain = new HealCandidate("Drain Health Other I", 0x800020CB, 39, 78, 0.8, 10, 2.5);

        Assert.False(read.NextHitKills(22));
        Assert.False(read.Survives(22, drain.CastSeconds));
        Assert.True(FightHealPick.HealSelfSavesSurer(in HealSelfI, in drain, in read, 22, out var self, out var other));
        Assert.True(self > other, $"{self} over {other}");
    }

    [Fact]
    public void A_heal_that_does_not_lift_her_over_the_next_blow_leaves_the_bigger_drain_M6_7_AA()
    {
        var read = R3cRead with { BiggestHit = 34 }; // a critical: 5 + 17.5 is still dead
        var drain = new HealCandidate("Drain Health Other I", 0x800020CB, 39, 78, 0.8, 10, 1.6);

        Assert.False(FightHealPick.HealSelfSavesSurer(in HealSelfI, in drain, in read, 5, out var self, out var other));
        Assert.Equal(0, self);
        Assert.True(other > 0);
    }

    [Fact]
    public void A_den_drain_quicker_and_as_sure_as_a_wound_up_heal_keeps_its_value_at_the_last_blow_the_rule_reads_the_numbers_M6_7_AA()
    {
        var read = new IncomingRead(30, 34, 1.5, 0.5, 3, true); // a blow due in 1 s
        var healSix = new HealCandidate("Heal Self VI", 0, 87.5, 0, 0.98, 30, 3.0);
        var drainOne = new HealCandidate("Drain Health Other I", 0x80001001, 120, 240, 0.95, 10, 0.9); // lands before the blow

        Assert.False(FightHealPick.HealSelfSavesSurer(in healSix, in drainOne, in read, 30, out var self, out var other));
        Assert.True(other > self, $"{other} over {self}");
    }

    [Theory]
    [InlineData(0.5, 1.5, 0.5, 0)] // lands before the blow falls due
    [InlineData(1.0, 0.8, 1.0, 2)] // overdue: one at once, one 0.8 s later
    [InlineData(1.6, 0.8, 1.0, 3)]
    [InlineData(2.5, 0.8, 1.0, 4)]
    public void The_blows_during_a_cast_are_read_from_the_recent_hits_M6_7_AA(double seconds, double interval, double since, int blows)
    {
        var read = new IncomingRead(10, 9, interval, since, 3, true);

        Assert.Equal(blows, FightHealPick.BlowsDuring(in read, seconds));
    }

    /// <summary>The emergency read with the next hit killing: a second tusker beside the mage (no finishing bolt), both at full health.</summary>
    private static (FightManaTests.Rig Rig, FightScenario Scenario) NextHitKills()
    {
        var (r, scenario, _, t) = Engaged();
        var health = Math.Max(6, MaxHealthOf(r) / 10);
        var extra = r.AddTusker(22);
        TuskerHealth(r, extra, 1f);
        TuskerHealth(r, t[0], 1f);
        Hit(r, scenario, health + 1, 0);
        r.Caster.SetHealth(health);
        return (r, scenario);
    }

    [Fact]
    public void Live_the_emergency_casts_heal_self_when_the_next_hit_kills_and_the_drain_scores_more_but_saves_less_M6_7_AA()
    {
        var (r, scenario) = NextHitKills();
        scenario.MeasuredHeals.NoteSeconds("Drain Health Other I", 2.0); // M6 14 tactics: quick enough to score more than Heal Self VI's 26.6 a second (3.26 s), too slow for the blow due in 1.9 s: each cast takes one blow, the resist decides

        r.Run(scenario, 4, 0.05, () => Logged(r, "the drain's cast outlasts her and Heal Self is the surer save") || Logged(r, "the drain stands"));

        Assert.True(Logged(r, "the drain's cast outlasts her and Heal Self is the surer save"));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("the drain stands", StringComparison.Ordinal));
    }

    [Fact]
    public void Live_a_drain_that_lands_before_the_blow_and_a_heal_that_lands_after_it_leave_the_drain_the_den_keeps_it_M6_7_AA()
    {
        var (r, scenario) = NextHitKills(); // Drain Health Other I in 1.6 s, Heal Self VI in 3 s, the next blow due in 1.9 s: she lives through the drain's cast

        r.Run(scenario, 4, 0.05, () => Logged(r, "the drain scores more than Heal Self") || Logged(r, "Heal Self is the surer save"));

        Assert.True(Logged(r, "the drain scores more than Heal Self"));
        Assert.False(Logged(r, "Heal Self is the surer save"));
    }
}
