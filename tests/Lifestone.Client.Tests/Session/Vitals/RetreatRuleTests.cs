using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>M6 6 F1: <see cref="RetreatRule"/>, every arm, the hold, 0 = never, the line and no allocation.</summary>
public sealed class RetreatRuleTests
{
    private static RetreatInput In(double health = 30, double incoming = 0, double heal = 0, bool nothing = false, bool aside = false, int line = 40, double now = 0) =>
        new(health, incoming, heal, nothing, aside, line, now);

    private static RetreatDecision Held(RetreatRule rule, RetreatInput i)
    {
        rule.Decide(i);
        return rule.Decide(i with { Now = i.Now + RetreatRule.RetreatHoldSeconds });
    }

    [Fact]
    public void The_stop_reason_is_named_retreat()
    {
        Assert.Equal("retreat", RetreatRule.StopReason.Name);
    }

    [Fact]
    public void The_incoming_beating_the_payable_heal_retreats_after_the_hold()
    {
        var rule = new RetreatRule();
        var first = rule.Decide(In(incoming: 20, heal: 10));
        Assert.False(first.Retreat);
        Assert.Equal("hold:incoming-beats-heal", first.Why);
        var later = rule.Decide(In(incoming: 20, heal: 10, now: RetreatRule.RetreatHoldSeconds));
        Assert.True(later.Retreat);
        Assert.Equal("incoming-beats-heal", later.Why);
    }

    [Fact]
    public void A_heal_that_keeps_up_with_the_incoming_never_retreats()
    {
        var rule = new RetreatRule();
        var d = Held(rule, In(incoming: 10, heal: 10));
        Assert.False(d.Retreat);
        Assert.Equal("clear", d.Why);
        Assert.False(Held(new RetreatRule(), In(incoming: 0, heal: 0)).Retreat); // nothing hurts the mage: nothing to flee
    }

    [Fact]
    public void Nothing_payable_retreats_after_the_hold()
    {
        var rule = new RetreatRule();
        Assert.Equal("hold:nothing-payable", rule.Decide(In(nothing: true)).Why);
        var d = rule.Decide(In(nothing: true, now: RetreatRule.RetreatHoldSeconds));
        Assert.True(d.Retreat);
        Assert.Equal("nothing-payable", d.Why);
    }

    [Fact]
    public void The_emergency_set_aside_retreats_after_the_hold()
    {
        var rule = new RetreatRule();
        Assert.Equal("hold:emergency-aside", rule.Decide(In(aside: true)).Why);
        var d = rule.Decide(In(aside: true, now: RetreatRule.RetreatHoldSeconds));
        Assert.True(d.Retreat);
        Assert.Equal("emergency-aside", d.Why);
    }

    [Fact]
    public void A_break_in_the_condition_restarts_the_hold()
    {
        var rule = new RetreatRule();
        rule.Decide(In(nothing: true, now: 0));
        Assert.False(rule.Decide(In(nothing: true, health: 80, now: 2)).Retreat); // healed above the line
        Assert.False(rule.Decide(In(nothing: true, now: 3)).Retreat); // the clock starts again here
        Assert.False(rule.Decide(In(nothing: true, now: 5.9)).Retreat);
        Assert.True(rule.Decide(In(nothing: true, now: 6)).Retreat);
    }

    [Fact]
    public void Health_at_or_over_the_line_never_retreats()
    {
        var rule = new RetreatRule();
        Assert.False(Held(rule, In(health: 40, nothing: true)).Retreat); // the line is "under"
        Assert.Equal("health-ok", rule.Decide(In(health: 90, nothing: true)).Why);
    }

    [Fact]
    public void A_line_of_zero_is_never()
    {
        var rule = new RetreatRule();
        var d = Held(rule, In(health: 1, incoming: 50, nothing: true, aside: true, line: 0));
        Assert.False(d.Retreat);
        Assert.Equal("off", d.Why);
    }

    [Fact]
    public void Reset_forgets_the_hold()
    {
        var rule = new RetreatRule();
        rule.Decide(In(nothing: true, now: 0));
        rule.Reset();
        Assert.False(rule.Decide(In(nothing: true, now: 10)).Retreat);
    }

    [Fact]
    public void The_line_reads_as_the_contract_says()
    {
        var i = In(health: 31.4, incoming: 12.5, heal: 8, nothing: false, aside: true, line: 40);
        Assert.Equal("retreat: decide health=31.4% incoming=12.5/s heal=8/s payable=yes aside=yes line=40% -> hold", RetreatRule.Line(i, new RetreatDecision(false, "x")));
        Assert.Equal("retreat: decide health=20% incoming=0/s heal=0/s payable=no aside=no line=50% -> retreat", RetreatRule.Line(In(health: 20, nothing: true, line: 50), new RetreatDecision(true, "x")));
    }

    [Fact]
    public void When_the_next_hit_kills_the_hold_is_waived_and_the_case_is_named_M6_7_Y()
    {
        var rule = new RetreatRule();
        var d = rule.Decide(In(incoming: 12, heal: 7) with { NextHitKills = true });
        Assert.True(d.Retreat);
        Assert.Equal("next-hit-kills", d.Why);
    }

    [Fact]
    public void A_heal_that_outruns_the_incoming_keeps_the_mage_in_the_fight_even_when_the_next_hit_kills_and_the_case_is_named_M6_7_Y()
    {
        var rule = new RetreatRule();
        var d = rule.Decide(In(incoming: 5, heal: 7) with { NextHitKills = true });
        Assert.False(d.Retreat);
        Assert.Equal("clear:next-hit-kills", d.Why);
    }

    [Fact]
    public void A_finishing_bolt_on_its_way_comes_before_the_retreat_and_the_hold_starts_again_after_it_M6_7_Y()
    {
        var rule = new RetreatRule();
        Assert.False(rule.Decide(In(incoming: 20, heal: 7, now: 0)).Retreat);
        var d = rule.Decide(In(incoming: 20, heal: 7, now: RetreatRule.RetreatHoldSeconds) with { NextHitKills = true, Finishing = true });
        Assert.False(d.Retreat);
        Assert.Equal("hold:finishing-bolt", d.Why);

        var after = rule.Decide(In(incoming: 20, heal: 7, now: RetreatRule.RetreatHoldSeconds + 0.1));
        Assert.False(after.Retreat); // a fresh hold: the bolt's kill stops the hits inside it
        Assert.Equal("hold:incoming-beats-heal", after.Why);
    }

    [Fact]
    public void The_line_names_the_next_hit_and_the_finishing_bolt_and_a_change_of_either_is_logged_M6_7_Y()
    {
        var rule = new RetreatRule();
        var i = In(health: 16.3, incoming: 11.5, heal: 7, line: 50);
        var d = rule.Decide(i);
        Assert.True(rule.ChangedSince(i, d));
        Assert.False(rule.ChangedSince(i, d));
        var named = i with { NextHitKills = true, Finishing = true };
        Assert.True(rule.ChangedSince(named, d));
        Assert.Equal("retreat: decide health=16.3% incoming=11.5/s heal=7/s payable=yes aside=no line=50% next-hit-kills finishing-bolt -> hold", RetreatRule.Line(named, new RetreatDecision(false, "x")));
    }

    [Fact]
    public void The_line_is_logged_only_when_it_changes()
    {
        var rule = new RetreatRule();
        var i = In(nothing: true);
        var d = rule.Decide(i);
        Assert.True(rule.ChangedSince(i, d));
        Assert.False(rule.ChangedSince(i, d));
        Assert.False(rule.ChangedSince(i with { Now = 1 }, d)); // time alone is not a change
        var later = i with { Now = 3 };
        var d2 = rule.Decide(later);
        Assert.True(rule.ChangedSince(later, d2)); // hold -> retreat
        Assert.False(rule.ChangedSince(later, d2));
        Assert.True(rule.ChangedSince(later with { HealthPercent = 25 }, d2));
    }

    [Fact]
    public void An_unread_health_is_its_own_token_and_never_a_retreat()
    {
        var rule = new RetreatRule();
        var i = In(health: double.NaN, nothing: true);
        var d = Held(rule, i);
        Assert.False(d.Retreat);
        Assert.Equal("health-unread", d.Why);
        Assert.NotEqual("health-ok", d.Why);
        Assert.True(rule.ChangedSince(i, d));
        Assert.False(rule.ChangedSince(i, d));
        var read = In(health: 80);
        Assert.True(rule.ChangedSince(read, rule.Decide(read)));
        Assert.Equal("retreat: decide health=unread incoming=0/s heal=0/s payable=no aside=no line=40% -> hold", RetreatRule.Line(i, d));
    }

    [Fact]
    [Trait("Tier", "Timing")] // an unread health decides without allocating, too
    public void An_unread_decision_allocates_nothing()
    {
        var rule = new RetreatRule();
        var i = In(health: double.NaN);
        rule.ChangedSince(i, rule.Decide(i));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var n = 0; n < 1000; n++)
        {
            rule.ChangedSince(i, rule.Decide(i with { Now = n }));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    [Trait("Tier", "Timing")] // the zero-byte allocation count flakes under load (m5-manaorder-alloc-flake)
    public void A_decision_allocates_nothing()
    {
        var rule = new RetreatRule();
        var input = In(incoming: 20, heal: 5, nothing: true);
        rule.Decide(input);
        rule.ChangedSince(input, rule.Decide(input));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var n = 0; n < 1000; n++)
        {
            var i = input with { Now = n, HealthPercent = 10 + (n % 50) };
            var d = rule.Decide(i);
            rule.ChangedSince(i, d);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
