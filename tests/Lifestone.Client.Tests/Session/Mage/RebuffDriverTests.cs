using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The re-buff driver (M4 4.1 lane a, <see cref="RebuffDriver"/>) on the fake clock against <see cref="ScriptedCaster"/> through <see cref="RebuffRig"/>: nothing before the margin, exactly one pass when the shortest line
/// reaches it, a busy lane or a fight or a death or a teleport waits (each a named reason, logged once), off does nothing, a failed pass backs off 60 s, a pass is never forced, and a tick between checks allocates nothing.
/// </summary>
public sealed class RebuffDriverTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const double Margin = RebuffRig.Margin;

    private static bool Resting(RebuffRig t) => t.Driver.State is RebuffDriverState.Idle or RebuffDriverState.BackingOff or RebuffDriverState.Off;

    [Fact]
    public void Nothing_is_cast_before_the_shortest_line_reaches_the_margin()
    {
        var t = new RebuffRig();
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(Margin + 30);
        var answered = t.Caster.Answered.Count;

        t.Advance(20); // checks run every 5 s: the soonest line still has more than the margin left
        Assert.True(t.Cycle.MinRemainingSeconds(t.Now) > Margin);
        Assert.Equal(RebuffDriverState.Idle, t.Driver.State);
        Assert.Equal(0, t.Driver.Passes);
        Assert.Equal(answered, t.Caster.Answered.Count);
        Assert.Null(t.Driver.LastResult);
        Assert.Null(t.Driver.WaitingFor);
    }

    [Fact]
    public void Exactly_one_unforced_pass_starts_when_the_shortest_line_reaches_the_margin()
    {
        var t = new RebuffRig();
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(Margin + 3);
        var answered = t.Caster.Answered.Count;
        t.Advance(2); // still outside the margin
        Assert.Equal(0, t.Driver.Passes);

        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1 && Resting(t), 120));
        Assert.Equal(1, t.Driver.Passes);
        Assert.Equal(RebuffDriverState.Idle, t.Driver.State);
        Assert.Null(t.Driver.LastFailure);
        var pass = t.Driver.LastResult!;
        output.WriteLine(pass.Fields);
        Assert.Equal("rebuff", pass.Pass);
        Assert.False(pass.Forced);
        Assert.True(pass.Cast >= 1);
        Assert.True(t.Caster.Answered.Count > answered);
        Assert.Equal(24, pass.Active);
        Assert.DoesNotContain("forced", string.Join('\n', t.DriverLines()), StringComparison.Ordinal);

        // The cycle's lines were cast within a few seconds of each other, so those not yet inside the margin when the pass began drift into it soon after: the next pass starts only when a line is due again, never earlier.
        var passes = 1;
        var end = t.Now + 300;
        while (t.Now < end)
        {
            t.Frame();
            if (t.Driver.Passes > passes)
            {
                passes = t.Driver.Passes;
                Assert.Equal(RebuffDriverState.Running, t.Driver.State);
                Assert.NotEmpty(t.Cycle.RebuffDue(t.Now, Margin)); // a line was due when this pass started
                Assert.True(t.RunUntil(() => Resting(t), 120));
                Assert.False(t.Driver.LastResult!.Forced);
            }
        }

        output.WriteLine("passes in 300 s after the first: " + (passes - 1));
    }

    [Fact]
    public void A_busy_lane_waits_and_the_pass_starts_when_it_frees()
    {
        var t = new RebuffRig();
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(Margin - 2); // a line is due

        const uint thing = 0x80000300;
        t.R.Create(thing, 11, missile: false);
        var use = t.R.Api.Use(thing, ActionSource.Test);
        t.R.Tick();
        Assert.False(use.IsCompleted); // in flight until the UseDone

        t.Advance(10, dt: 1); // the driver sees the due line and the busy lane at every tick and check (the Use would time out at 20 s)
        Assert.Equal(RebuffDriverState.Waiting, t.Driver.State);
        Assert.Equal(RebuffDriver.GuardLane, t.Driver.WaitingFor);
        Assert.Equal(0, t.Driver.Passes);
        Assert.Single(t.DriverLines(), l => l.Contains("pass waits: " + RebuffDriver.GuardLane, StringComparison.Ordinal)); // logged once, not per tick

        t.R.UseDone();
        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1 && Resting(t), 120));
        Assert.Equal(1, t.Driver.Passes);
        Assert.Null(t.Driver.WaitingFor);
        Assert.Equal(24, t.Driver.LastResult!.Active);
        Assert.Contains(t.DriverLines(), l => l.Contains("pass 1 done", StringComparison.Ordinal));
    }

    [Fact]
    public void Off_does_nothing_until_it_is_switched_on()
    {
        var t = new RebuffRig(auto: false);
        Assert.False(t.Driver.Enabled);
        Assert.Equal(RebuffDriverState.Off, t.Driver.State);
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(Margin - 2);
        var answered = t.Caster.Answered.Count;

        t.Advance(60);
        Assert.Equal(RebuffDriverState.Off, t.Driver.State);
        Assert.Equal(0, t.Driver.Passes);
        Assert.Equal(answered, t.Caster.Answered.Count);

        t.Driver.Enabled = true;
        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1 && Resting(t), 120));
        Assert.Equal(1, t.Driver.Passes);
        Assert.False(t.Driver.LastResult!.Forced);
    }

    [Fact]
    public void A_pass_that_missed_a_line_backs_off_sixty_seconds_and_logs_why()
    {
        var t = new RebuffRig();
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(Margin - 2);
        t.Caster.FizzleNext(_ => true, 2); // the first line due fizzles, and its retry one level down fizzles: a miss

        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1 && Resting(t), 120));
        Assert.Equal(RebuffDriverState.BackingOff, t.Driver.State);
        Assert.NotNull(t.Driver.LastFailure);
        Assert.Contains("missed", t.Driver.LastFailure, StringComparison.Ordinal);
        Assert.Equal(1, t.Driver.Passes);
        var ended = t.Now;
        Assert.InRange(t.Driver.NextCheckAt - ended, RebuffDriver.BackoffSeconds - 1, RebuffDriver.BackoffSeconds + 0.001);
        Assert.Contains(t.DriverLines(), l => l.Contains("did not succeed", StringComparison.Ordinal) && l.Contains("backing off 60 s", StringComparison.Ordinal));

        // The missed line is still due, but nothing starts inside the back-off.
        t.Advance(RebuffDriver.BackoffSeconds - 3);
        Assert.Equal(RebuffDriverState.BackingOff, t.Driver.State);
        Assert.Equal(1, t.Driver.Passes);

        // After it the driver tries again (the fizzles are spent) and the pass lands.
        Assert.True(t.RunUntil(() => t.Driver.Passes >= 2 && Resting(t), 120));
        Assert.Equal(2, t.Driver.Passes);
        Assert.Null(t.Driver.LastFailure);
        Assert.Equal(RebuffDriverState.Idle, t.Driver.State);
        Assert.Equal(24, t.Driver.LastResult!.Active);
    }

    [Fact]
    public void A_pass_the_cycle_fails_backs_off_sixty_seconds_too()
    {
        var t = new RebuffRig(new BuffOptions { RebuffMarginSeconds = RebuffRig.Margin, ManaWaitSeconds = 1 });
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(Margin - 2);
        t.Caster.Mana = 0; // nothing to cast with, no transfer or revitalize possible, and no regeneration: the mana wait runs out
        t.Caster.Stamina = 0;
        t.Caster.ManaRegenPerSecond = 0;
        t.Caster.StaminaRegenPerSecond = 0;

        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1 && Resting(t), 120));
        Assert.Equal(RebuffDriverState.BackingOff, t.Driver.State);
        Assert.StartsWith("the pass failed:", t.Driver.LastFailure, StringComparison.Ordinal);
        Assert.NotNull(t.Driver.LastResult!.Failure);
        Assert.Equal(1, t.Driver.Passes);
        t.Advance(RebuffDriver.BackoffSeconds - 5);
        Assert.Equal(1, t.Driver.Passes);
    }

    [Fact]
    public void A_fight_pauses_the_pass_by_combat_mode_and_by_damage_taken()
    {
        var t = new RebuffRig();
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(Margin - 2);

        t.R.CombatModeIs(CombatModes.Melee);
        t.Advance(15);
        Assert.Equal(RebuffDriverState.Waiting, t.Driver.State);
        Assert.Equal(RebuffDriver.GuardFight, t.Driver.WaitingFor);
        Assert.Equal(0, t.Driver.Passes);

        // Back in peace but hit a moment ago: the last 10 s count as a fight.
        t.R.CombatModeIs(CombatModes.NonCombat);
        t.Hit();
        var hitAt = t.Now;
        t.Advance(5);
        Assert.Equal(RebuffDriver.GuardFight, t.Driver.WaitingFor);
        Assert.Equal(0, t.Driver.Passes);

        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1, 60));
        Assert.True(t.Now - hitAt >= RebuffDriver.FightQuietSeconds, "the pass started inside the 10 s after the damage");
        Assert.True(t.RunUntil(() => Resting(t), 120));
        Assert.Equal(24, t.Driver.LastResult!.Active);
    }

    [Fact]
    public void The_pass_enters_the_magic_mode_and_puts_the_player_back_in_peace()
    {
        var t = new RebuffRig(magic: false);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        t.R.CombatModeIs(CombatModes.Magic);
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.R.CombatModeIs(CombatModes.NonCombat);
        t.Caster.ModesSent.Clear();
        t.SkipTo(Margin - 2);

        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1 && Resting(t), 120));
        Assert.Equal([CombatModes.Magic, CombatModes.NonCombat], t.Caster.ModesSent);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        Assert.Null(t.Driver.LastFailure);
        Assert.Equal(24, t.Driver.LastResult!.Active);
    }

    [Fact]
    public void A_pass_that_started_in_the_magic_mode_leaves_it_alone()
    {
        var t = new RebuffRig();
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.Caster.ModesSent.Clear();
        t.SkipTo(Margin - 2);

        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1 && Resting(t), 120));
        Assert.Empty(t.Caster.ModesSent);
        Assert.Equal(CombatModes.Magic, t.R.H.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void A_mage_with_no_buffs_gets_an_unforced_pass_that_casts_every_line()
    {
        var t = new RebuffRig();
        Assert.True(t.RunUntil(() => t.Driver.Passes >= 1 && Resting(t), 600));
        var pass = t.Driver.LastResult!;
        Assert.Equal("rebuff", pass.Pass);
        Assert.False(pass.Forced);
        Assert.Null(t.Driver.LastFailure);
        Assert.Equal(24, pass.Active);
        Assert.False(pass.Rebuffed); // nothing was active to recast
    }

    [Fact]
    public void Not_in_the_world_dead_and_portal_space_each_wait_under_their_own_name()
    {
        // Not in the world: the player has not been created yet.
        var early = new RebuffRig(present: false);
        early.Advance(12, dt: 1);
        Assert.Equal(RebuffDriverState.Waiting, early.Driver.State);
        Assert.Equal(RebuffDriver.GuardNotInWorld, early.Driver.WaitingFor);
        Assert.Equal(0, early.Driver.Passes);

        var t = new RebuffRig();
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(Margin - 2);

        t.Health(0);
        t.Advance(8);
        Assert.Equal(RebuffDriver.GuardDead, t.Driver.WaitingFor);
        Assert.Equal(0, t.Driver.Passes);

        t.Health(150);
        t.Teleport();
        t.Advance(8);
        Assert.Equal(RebuffDriver.GuardPortalSpace, t.Driver.WaitingFor);
        Assert.Equal(0, t.Driver.Passes);

        // Each reason was logged once, in the order they held.
        var waits = t.DriverLines().Where(l => l.Contains("pass waits:", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, waits.Count);
        Assert.Contains(RebuffDriver.GuardDead, waits[0], StringComparison.Ordinal);
        Assert.Contains(RebuffDriver.GuardPortalSpace, waits[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_guard_that_holds_for_two_minutes_with_a_line_due_warns_naming_the_line()
    {
        var t = new RebuffRig();
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(Margin - 2);
        t.R.CombatModeIs(CombatModes.Melee);

        t.Advance(100, dt: 1);
        Assert.DoesNotContain(t.R.H.Log.Entries, e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Warning && e.Category == BuffCycle.LogCategory);

        t.Advance(40, dt: 1);
        var warnings = t.R.H.Log.Entries.Where(e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Warning && e.Category == BuffCycle.LogCategory).ToList();
        var warning = Assert.Single(warnings);
        Assert.Contains(RebuffDriver.GuardFight, warning.Message, StringComparison.Ordinal);
        Assert.Contains(t.Cycle.RebuffDue(t.Now, Margin)[0], warning.Message, StringComparison.Ordinal);
        Assert.Contains("is due", warning.Message, StringComparison.Ordinal);
        Assert.Equal(0, t.Driver.Passes);
    }

    [Fact]
    public void A_tick_between_checks_allocates_nothing()
    {
        var t = new RebuffRig();
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.Advance(6); // a check has run and set the next one 5 s ahead
        var now = t.Now;
        Assert.True(t.Driver.NextCheckAt > now);
        t.Driver.Tick(now); // warm up

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            t.Driver.Tick(now);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        // A waiting driver's ticks between checks (the guards only) allocate nothing either.
        t.SkipTo(Margin - 2);
        t.R.CombatModeIs(CombatModes.Melee);
        t.Advance(6);
        Assert.Equal(RebuffDriverState.Waiting, t.Driver.State);
        now = t.Now;
        Assert.True(t.Driver.NextCheckAt > now);
        t.Driver.Tick(now);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            t.Driver.Tick(now);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
