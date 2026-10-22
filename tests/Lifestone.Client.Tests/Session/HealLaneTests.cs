using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.1 s1: the Heal lane. One heal in flight at a time, in call order; after a confirmed use the lane holds the next heal until the UseDone AND the player's own Ready F74C AND 0.1 s have passed, because ACE is busy until the Ready
/// (capture.md C2: a repeat sent 0.2 s after the first was refused 0x001D at once, and one sent AT the UseDone, 8 ms before the Ready, was refused 0x001D too). A refused use holds nothing.
/// </summary>
public sealed class HealLaneTests
{
    private const uint Player = HealRig.Player;
    private const uint Kit = HealRig.Kit;
    private const uint Kit2 = HealRig.Kit2;

    /// <summary>The first kit use up to the UseDone: sent, the heal motion, +1.48 s, the line, the Structure and the UseDone 0 (no Ready yet).</summary>
    private static (HealRig Rig, HealHandle First) UseDoneAfterKit()
    {
        var r = new HealRig();
        var first = r.HealSelf();
        r.Tick(0.01);
        r.ActionMotion(HealRig.SkillHealSelf);
        r.Tick(1.47);
        r.Line("You fail to heal yourself. Your Handy Healing Kit has 19 uses left.");
        r.Structure(Kit, 19);
        r.UseDone();
        return (r, first);
    }

    [Fact]
    public void The_lanes_public_margin_is_the_policys_0_1_s_that_the_ticks_below_pin_3_1_fix_R25()
    {
        // The heal-kit play-check row (Lifestone.Game) judges the second heal's send against this constant: it must be the number this class's ticks exercise (90 ms inside the margin, 110 ms sends).
        Assert.Equal(0.1, HealLaneTiming.MarginSeconds);
        Assert.Equal(ActionPolicy.BusyChainMarginSeconds, HealLaneTiming.MarginSeconds);
    }

    [Fact]
    public void A_second_heal_waits_for_the_UseDone_then_for_the_Ready_plus_0_1_s()
    {
        var (r, first) = UseDoneAfterKit();
        var second = r.Api.UseWithTarget(Kit2, Player, ActionSource.Ui);
        r.Tick(); // the first's UseDone is in; its Ready is not (capture C1: it comes 10 to 30 ms after)
        Assert.Equal(ActionOutcomeKind.Confirmed, first.Outcome!.Value.Kind);
        Assert.Equal(ActionStatus.Queued, second.Status);
        Assert.Single(r.R.H.Net.Of(FakeSendKind.UseWithTarget)); // a repeat at the UseDone is refused 0x001D by ACE: it is not sent

        r.Tick(0.03);
        r.Ready();
        r.Tick();
        Assert.Equal(ActionStatus.Queued, second.Status);
        r.Tick(0.09);
        Assert.Equal(ActionStatus.Queued, second.Status); // 90 ms after the Ready: inside the margin
        r.Tick(0.02);
        Assert.Equal(ActionStatus.Sent, second.Status);

        var sends = r.R.H.Net.Of(FakeSendKind.UseWithTarget);
        Assert.Equal(2, sends.Count);
        Assert.Equal(Kit2, sends[1].Guid);
    }

    [Fact]
    public void A_heal_sent_before_the_UseDone_waits_through_the_whole_use()
    {
        var r = new HealRig();
        var first = r.HealSelf();
        var second = r.Api.UseWithTarget(Kit2, Player, ActionSource.Ui);
        r.Tick(0.2); // capture C2: a repeat 0.2 s into a use is refused 0x001D: the lane does not send it
        Assert.Equal(ActionStatus.Queued, second.Status);
        r.Tick(1.2);
        Assert.False(first.IsCompleted);
        Assert.Equal(ActionStatus.Queued, second.Status);
        Assert.Single(r.R.H.Net.Of(FakeSendKind.UseWithTarget));
    }

    [Fact]
    public void Without_a_Ready_F74C_the_hold_ends_0_25_s_plus_0_1_s_after_the_UseDone()
    {
        var (r, _) = UseDoneAfterKit();
        var second = r.Api.UseWithTarget(Kit2, Player, ActionSource.Ui);
        r.Tick();
        r.Tick(0.3); // the heal motion's own F74C carries a command: it is not the Ready
        Assert.Equal(ActionStatus.Queued, second.Status);
        r.Tick(0.1);
        Assert.Equal(ActionStatus.Sent, second.Status);
    }

    [Fact]
    public void A_potion_s_Ready_before_the_UseDone_leaves_only_the_margin_after_the_UseDone()
    {
        var r = new HealRig();
        var potion = r.DrinkPotion();
        r.Tick(0.01);
        r.ActionMotion(HealRig.Drink);
        r.Tick(0.73);
        r.Line("The Potion of Healing restores 25 points of your Health.");
        r.Stack(HealRig.Potion, 4);
        r.Tick(0.02);
        r.Ready(); // C3: the Ready at +0.76 s, the UseDone at +1.48 s
        r.Tick(0.72);
        r.UseDone();
        var kit = r.Api.UseWithTarget(Kit, Player, ActionSource.Ui);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, potion.Outcome!.Value.Kind);
        Assert.Equal(ActionStatus.Queued, kit.Status);

        r.Tick(0.09);
        Assert.Equal(ActionStatus.Queued, kit.Status);
        r.Tick(0.02);
        Assert.Equal(ActionStatus.Sent, kit.Status);
    }

    [Fact]
    public void A_heal_queued_behind_a_failed_roll_goes_after_that_use_s_hold_and_not_before()
    {
        var (r, first) = UseDoneAfterKit();
        var second = r.Api.UseWithTarget(Kit2, Player, ActionSource.Ui);
        var third = r.Api.Drink(HealRig.Potion, ActionSource.Ui);
        r.Tick();
        r.Tick(0.01);
        r.Ready();
        r.Tick(0.11);

        Assert.Equal(HealResultKind.FailedRoll, first.Result!.Kind);
        Assert.Equal(ActionStatus.Sent, second.Status);
        Assert.Equal(ActionStatus.Queued, third.Status); // one in flight at a time: the potion waits for the second kit's use

        r.Tick(1.48);
        r.Structure(Kit2, 19);
        r.Line("You fail to heal yourself. Your Handy Healing Kit has 19 uses left.");
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, second.Outcome!.Value.Kind);
        Assert.Equal(ActionStatus.Queued, third.Status);
        r.Ready();
        r.Tick(0.11);
        Assert.Equal(ActionStatus.Sent, third.Status);
        Assert.Single(r.R.H.Net.Of(FakeSendKind.Use));
    }

    [Fact]
    public void A_refused_use_holds_nothing_so_the_next_heal_goes_at_once()
    {
        var r = new HealRig();
        var first = r.HealSelf();
        var second = r.Api.UseWithTarget(Kit2, Player, ActionSource.Ui);
        r.Tick(0.002);
        r.UseDone(0x001D); // ACE ran no chain for the refused use
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, first.Outcome!.Value.Kind);
        Assert.Equal(ActionStatus.Sent, second.Status);
    }

    [Fact]
    public void A_Use_waits_for_a_heal_in_flight_because_ACE_s_UseDone_names_no_request()
    {
        var r = new HealRig();
        var heal = r.HealSelf();
        var use = r.Api.Use(0x80000777, ActionSource.Ui);
        r.Tick(0.5);
        Assert.Equal(ActionStatus.Queued, use.Status);

        r.Tick(1.0);
        r.Structure(Kit, 19);
        r.Line("You fail to heal yourself. Your Handy Healing Kit has 19 uses left.");
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, heal.Outcome!.Value.Kind);
        Assert.False(use.IsCompleted);
        r.Ready();
        r.Tick(0.11);
        Assert.Equal(ActionStatus.Sent, use.Status);
        Assert.Single(r.R.H.Net.Of(FakeSendKind.Use));
    }

    [Fact]
    public void The_lane_is_its_own_a_move_in_the_Inventory_lane_does_not_wait_for_a_heal()
    {
        var r = new HealRig();
        var heal = r.HealSelf();
        var drop = r.Api.Drop(Kit2, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionStatus.Sent, heal.Status);
        Assert.Equal(ActionStatus.Sent, drop.Status);
    }

    [Fact]
    public void The_Heal_lane_exists_in_the_lane_count()
    {
        Assert.Equal((int)ActionLane.Immediate + 1, ActionPolicy.LaneCount);
        Assert.True(ActionLane.Heal < ActionLane.Immediate);
        Assert.Null(ActionPolicy.CreateLimiter(ActionLane.Heal));
    }
}
