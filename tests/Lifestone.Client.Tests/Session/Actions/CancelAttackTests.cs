using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <c>CancelAttack()</c> (GameAction 0x01B7; M4 1.2 s2): ACE finishes the swing in progress and ends the sequence with 0x01A7 {0x0036} (W/Player_Melee.cs 240-250), which completes it; with no sequence running it answers
/// nothing, so the action completes as Sent after the send. And the argument checks of the three attack members.
/// </summary>
public sealed class CancelAttackTests
{
    [Fact]
    public void With_no_sequence_running_it_sends_0x01B7_and_completes_at_once_as_Sent()
    {
        var r = new AttackRig();

        var cancel = r.Api.CancelAttack(ActionSource.Automation);
        r.Tick();

        Assert.Single(r.Net.Of(FakeSendKind.CancelAttack));
        Assert.Equal(ActionOutcomeKind.Sent, cancel.Outcome!.Value.Kind);
        Assert.Equal(ActionKind.CancelAttack, cancel.Kind);
    }

    [Fact]
    public void With_a_sequence_running_it_waits_for_the_sequence_s_end_and_not_for_a_swing_over()
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);

        var cancel = r.Api.CancelAttack(ActionSource.Automation);
        r.Tick();
        Assert.Single(r.Net.Of(FakeSendKind.CancelAttack));
        Assert.False(cancel.IsCompleted);

        r.Done(0); // the current swing finishes first
        r.Tick();
        Assert.False(cancel.IsCompleted);

        r.Done(AttackRig.Over);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, cancel.Outcome!.Value.Kind);
        Assert.Equal(AttackRig.Over, cancel.Outcome.Value.Code);
    }

    [Fact]
    public void A_sequence_that_never_ends_times_the_cancel_out_at_5_seconds()
    {
        var r = new AttackRig();
        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        var cancel = r.Api.CancelAttack(ActionSource.Automation);
        r.Tick();

        r.Tick(4.5);
        Assert.False(cancel.IsCompleted);
        r.Tick(1);

        Assert.Equal(ActionOutcomeKind.TimedOut, cancel.Outcome!.Value.Kind);
        Assert.Equal(5.0, ActionPolicy.CancelAttackSeconds);
    }

    [Fact]
    public void The_cancel_waits_in_the_attack_lane_behind_an_attack_not_yet_confirmed()
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        var attack = r.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.5f, ActionSource.Automation);
        var cancel = r.Api.CancelAttack(ActionSource.Automation);
        r.Tick();
        Assert.Empty(r.Net.Of(FakeSendKind.CancelAttack));

        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Tick();

        Assert.True(attack.IsCompleted);
        Assert.Single(r.Net.Of(FakeSendKind.CancelAttack));
        Assert.False(cancel.IsCompleted);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    public void A_power_or_accuracy_that_is_not_finite_or_outside_0_to_1_is_refused_and_nothing_is_sent(float bar)
    {
        var r = new AttackRig();

        var melee = r.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, bar, ActionSource.Automation);
        var missile = r.Api.MissileAttack(AttackRig.Reedshark, AttackHeight.Medium, bar, ActionSource.Automation);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, melee.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, missile.Outcome!.Value.Kind);
        Assert.Contains("power", melee.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("accuracy", missile.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(r.Net.Of(FakeSendKind.MeleeAttack));
        Assert.Empty(r.Net.Of(FakeSendKind.MissileAttack));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(4u)]
    public void A_height_that_is_not_1_2_or_3_is_refused(uint height)
    {
        var r = new AttackRig();

        var melee = r.Api.Attack(AttackRig.Reedshark, (AttackHeight)height, 0.5f, ActionSource.Automation);
        var missile = r.Api.MissileAttack(AttackRig.Reedshark, (AttackHeight)height, 0.5f, ActionSource.Automation);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, melee.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, missile.Outcome!.Value.Kind);
        Assert.Empty(r.Net.Of(FakeSendKind.MeleeAttack));
    }

    [Fact]
    public void No_target_is_refused_and_the_edges_0_and_1_and_all_three_heights_are_sent()
    {
        var r = new AttackRig();

        var none = r.Api.Attack(0, AttackHeight.Medium, 0.5f, ActionSource.Automation);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, none.Outcome!.Value.Kind);
        Assert.Empty(r.Net.Of(FakeSendKind.MeleeAttack));

        foreach (var (height, bar) in new[] { (AttackHeight.High, 0f), (AttackHeight.Medium, 1f), (AttackHeight.Low, 0.5f) })
        {
            r.Api.Attack(AttackRig.Reedshark, height, bar, ActionSource.Automation);
            r.Approach(AttackRig.Reedshark);
            r.Tick();
            r.Tick();
        }

        Assert.Equal([1u, 2u, 3u], r.Net.Of(FakeSendKind.MeleeAttack).Select(s => s.Amount).ToArray());
    }
}
