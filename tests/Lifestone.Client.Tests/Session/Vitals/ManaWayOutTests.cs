using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>M6 7 BE: <see cref="ManaWayOut"/>, the leaving and the order of the spending. The numbers are the Lodge's at level 22: the recall 50, Drain Health Other I 10, the bolt 20.</summary>
public sealed class ManaWayOutTests
{
    private static readonly WayOutLine Lodge = ManaWayOut.LineOf(50, 10);

    private static WayOutInput In(uint mana, bool ready = true, bool attackers = true, bool kill = false, bool press = false, bool refused = false) =>
        new(ready, mana, Lodge, attackers, kill, press, refused);

    private static WayOutSpend Bolt(uint mana, bool attackers = true, bool kill = false) => new(WayOutSpendKind.Bolt, mana, 20, Lodge, attackers, kill);

    private static WayOutSpend Buff(uint mana, bool attackers = true) => new(WayOutSpendKind.Buff, mana, 40, Lodge, attackers);

    private static WayOutSpend Drain(uint mana, bool nextHitKills = false, bool attackers = true, uint cost = 10) => new(WayOutSpendKind.Drain, mana, cost, Lodge, attackers, NextHitKills: nextHitKills);

    private static WayOutAnswer Decide(WayOutInput i) => ManaWayOut.Decide(in i);

    private static WayOutAnswer Spend(WayOutSpend s) => ManaWayOut.Spend(in s);

    [Fact]
    public void The_line_is_the_recalls_need_plus_one_drain_and_is_named_on_the_log()
    {
        Assert.Equal(new WayOutLine(60, 50, 10), Lodge);
        Assert.Equal(new WayOutLine(130, 120, 10), ManaWayOut.LineOf(120, 10)); // a big pool: the station's share of the mana is the need, not a number here
        Assert.Equal("line=60 (Lifestone Recall 50 + Drain Health Other I 10)", ManaWayOut.Named(in Lodge));
    }

    [Theory]
    [InlineData(60u)]
    [InlineData(59u)]
    [InlineData(50u)] // the last mana that pays the recall
    public void At_or_under_the_line_with_attackers_on_her_she_leaves(uint mana)
    {
        var d = Decide(In(mana));
        Assert.True(d.Yes);
        Assert.Equal(ManaWayOut.Leave, d.Why);
    }

    [Fact]
    public void Over_the_line_she_stays()
    {
        var d = Decide(In(61));
        Assert.False(d.Yes);
        Assert.Equal("over-the-line", d.Why);
    }

    [Fact]
    public void A_refused_spend_over_the_line_leaves_too_she_never_stands_holding_mana_she_may_not_spend()
    {
        Assert.Equal(new WayOutAnswer(true, ManaWayOut.Leave), Decide(In(75, refused: true)));
    }

    [Fact]
    public void Without_the_spell_she_keeps_the_old_order()
    {
        var d = Decide(In(5, ready: false));
        Assert.False(d.Yes);
        Assert.Equal("no-recall", d.Why);
    }

    [Fact]
    public void Out_of_a_fight_the_rule_has_no_effect()
    {
        var d = Decide(In(5, attackers: false));
        Assert.False(d.Yes);
        Assert.Equal("no-attackers", d.Why);
        Assert.Equal("no-attackers", Spend(Bolt(5, attackers: false)).Why);
        Assert.True(Spend(Bolt(5, attackers: false)).Yes);
        Assert.True(Spend(Buff(45, attackers: false)).Yes);
        Assert.True(Spend(Drain(45, attackers: false)).Yes);
    }

    [Fact]
    public void A_kill_that_clears_the_last_attacker_waits_and_comes_before_the_leaving()
    {
        var d = Decide(In(55, kill: true));
        Assert.False(d.Yes);
        Assert.Equal("hold:kill-pending", d.Why);
        Assert.True(Decide(In(55)).Yes); // the kill gone, she leaves at once
    }

    [Fact]
    public void A_press_on_its_way_comes_before_the_leaving()
    {
        var d = Decide(In(55, press: true));
        Assert.False(d.Yes);
        Assert.Equal("hold:press", d.Why);
    }

    [Fact]
    public void Under_the_recalls_need_there_is_no_leaving_and_the_case_is_named()
    {
        var d = Decide(In(49));
        Assert.False(d.Yes);
        Assert.Equal("recall-gone", d.Why);
    }

    [Theory]
    [InlineData(81u, true, "over-the-line")]
    [InlineData(80u, true, "over-the-line")] // lands on the line exactly
    [InlineData(79u, false, "bolt-refused")]
    [InlineData(60u, false, "bolt-refused")]
    [InlineData(50u, false, "bolt-refused")]
    public void A_bolt_is_refused_when_it_takes_the_mana_under_the_line(uint mana, bool yes, string why)
    {
        var d = Spend(Bolt(mana));
        Assert.Equal(yes, d.Yes);
        Assert.Equal(why, d.Why);
    }

    [Theory]
    [InlineData(79u)]
    [InlineData(30u)]
    public void The_bolt_that_kills_the_last_attacker_goes_whatever_it_leaves(uint mana)
    {
        Assert.Equal(new WayOutAnswer(true, "kill-of-last-attacker"), Spend(Bolt(mana, kill: true)));
    }

    [Fact]
    public void Under_the_recalls_need_the_mana_is_no_longer_saved_for_it_and_a_bolt_goes()
    {
        Assert.Equal(new WayOutAnswer(true, "recall-gone"), Spend(Bolt(49)));
        Assert.Equal(new WayOutAnswer(true, "recall-gone"), Spend(Drain(49)));
    }

    [Theory]
    [InlineData(100u, true, "over-the-line")]
    [InlineData(99u, false, "buff-refused")]
    [InlineData(49u, false, "buff-refused")] // a buff never goes under the line: not even with the recall gone
    public void A_buff_never_takes_the_mana_under_the_line(uint mana, bool yes, string why)
    {
        var d = Spend(Buff(mana));
        Assert.Equal(yes, d.Yes);
        Assert.Equal(why, d.Why);
    }

    [Theory]
    [InlineData(70u, false, true, "over-the-line")]
    [InlineData(69u, false, true, "drain-recall-paid")] // into the line, the recall still paid
    [InlineData(60u, false, true, "drain-recall-paid")]
    [InlineData(59u, false, false, "drain-refused")] // it would leave the recall unpaid
    [InlineData(50u, false, false, "drain-refused")]
    [InlineData(59u, true, true, "drain-next-hit-kills")] // the next hit kills: a recall that comes too late saves nothing
    [InlineData(50u, true, true, "drain-next-hit-kills")]
    public void The_drain_spends_into_the_line_only_with_the_recall_paid_or_the_next_hit_killing(uint mana, bool nextHit, bool yes, string why)
    {
        var d = Spend(Drain(mana, nextHit));
        Assert.Equal(yes, d.Yes);
        Assert.Equal(why, d.Why);
    }

    [Fact]
    public void A_dear_drain_is_weighed_at_its_own_cost()
    {
        Assert.False(Spend(Drain(85, cost: 40)).Yes); // 45 would not pay the recall
        Assert.True(Spend(Drain(90, cost: 40)).Yes);
    }

    [Fact]
    public void The_decision_line_is_named_apart_from_the_healths()
    {
        var i = In(58, kill: false, press: true);
        var d = Decide(i);
        Assert.Equal("retreat: mana: decide mana=58 line=60 (Lifestone Recall 50 + Drain Health Other I 10) attackers=yes kill-pending=no press=yes dance=no refused=no potion=no losing=no -> hold (hold:press)", ManaWayOut.DecideLine(in i, in d));
        var go = In(58);
        var left = Decide(go);
        Assert.EndsWith("-> retreat (mana-line)", ManaWayOut.DecideLine(in go, in left), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "Timing")] // the zero-byte allocation count flakes under load (m5-manaorder-alloc-flake)
    public void A_decision_allocates_nothing()
    {
        var input = In(55);
        var spend = Bolt(70);
        ManaWayOut.Decide(in input);
        ManaWayOut.Spend(in spend);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var n = 0; n < 1000; n++)
        {
            var i = input with { Mana = (uint)(40 + (n % 40)) };
            ManaWayOut.Decide(in i);
            var s = spend with { Mana = (uint)(40 + (n % 40)) };
            ManaWayOut.Spend(in s);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(55u, false)] // at the line
    [InlineData(75u, true)] // over it, the bolt refused
    public void A_Mana_Potion_in_use_comes_before_the_leaving_M6_8(uint mana, bool refused)
    {
        var input = In(mana, refused: refused) with { Potion = true };
        var d = Decide(input);
        Assert.False(d.Yes);
        Assert.Equal("hold:potion", d.Why);
        Assert.EndsWith("potion=yes losing=no -> hold (hold:potion)", ManaWayOut.DecideLine(in input, in d), StringComparison.Ordinal);
        Assert.True(Decide(input with { Potion = false }).Yes); // none held: she leaves as before
    }

    [Fact]
    public void In_a_fight_she_is_losing_the_potions_hold_no_leaving_M6_8()
    {
        // Live 2026-11-11 18:52 UTC, the Cave: five tuskers hit her for 10 a second at health 76, mana 52 at the line 62 with 83 potions held: the leaving was held, the drains spent the recall and she died at mana 0.
        Assert.Equal(5, ManaWayOut.LosingSeconds); // the owner, 2026-11-11: fleeing is for a mistake, not a habit
        Assert.False(ManaWayOut.IsLosing(76, 10.6)); // seven seconds of it left: she fights on
        Assert.True(ManaWayOut.IsLosing(50, 10)); // five seconds exactly
        Assert.False(ManaWayOut.IsLosing(51, 10));
        Assert.True(ManaWayOut.IsLosing(40, 10.6));
        Assert.False(ManaWayOut.IsLosing(76, 3)); // one tusker: the potions hold
        Assert.False(ManaWayOut.IsLosing(0, 0)); // no hits: nothing is losing

        var input = In(55) with { Potion = true, Losing = true };
        var d = Decide(input);
        Assert.True(d.Yes);
        Assert.Equal(ManaWayOut.Leave, d.Why);
        Assert.EndsWith("potion=yes losing=yes -> retreat (mana-line)", ManaWayOut.DecideLine(in input, in d), StringComparison.Ordinal);
        Assert.False(Decide(input with { Mana = 49 }).Yes); // the recall is gone: nothing to leave by
        Assert.False(Decide(input with { Press = true }).Yes); // a press on its way holds as ever
    }

    [Fact]
    public void A_potion_never_holds_her_once_the_recall_is_unpaid_or_gives_a_leaving_without_attackers_M6_8()
    {
        Assert.Equal("recall-gone", Decide(In(49) with { Potion = true }).Why);
        Assert.Equal("no-attackers", Decide(In(5, attackers: false) with { Potion = true }).Why);
    }

    [Theory]
    [InlineData(30u, true)] // the bolt 20 and one drain 10
    [InlineData(29u, false)]
    [InlineData(78u, true)] // the live case: over the old line by less than the bolt
    public void With_potions_in_use_the_bolt_goes_while_the_mana_pays_it_and_one_drain_M6_8(uint mana, bool goes) =>
        Assert.Equal(goes, ManaWayOut.BoltOnPotions(mana, 20, in Lodge));
}
