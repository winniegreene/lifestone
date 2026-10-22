using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <c>Attack(target, height, power)</c> (GameAction 0x0008; M4 1.2 s2) over the live order of cache/m4/1-2/capture.md C1: ACE walks the player (MoveToObject F74C), swings (the own attack F74C), and repeats by itself, so
/// the tracker completes at the first sign ACE took the send (the sequence end is the attack sequence's). A refusal is a WeenieError or a transient string, then 0x01A7; a refusal with neither is silent.
/// </summary>
public sealed class MeleeAttackTrackerTests
{
    private static AttackRig Start(out ActionHandle attack, float power = 0.5f)
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        attack = r.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, power, ActionSource.Automation);
        r.Tick();
        return r;
    }

    [Fact]
    public void The_send_is_0x0008_with_the_height_and_power_and_waits_for_a_sign()
    {
        var r = Start(out var attack);

        var sent = Assert.Single(r.Net.Of(FakeSendKind.MeleeAttack));
        Assert.Equal(AttackRig.Reedshark, sent.Guid);
        Assert.Equal(2u, sent.Amount);
        Assert.Equal(0.5f, sent.Extent);
        Assert.False(attack.IsCompleted);
        Assert.Equal(ActionKind.Attack, attack.Kind);
    }

    [Fact]
    public void The_approach_F74C_naming_the_target_confirms_before_any_swing()
    {
        var r = Start(out var attack);

        r.Approach(AttackRig.Other); // another object's approach is not ours
        r.Tick();
        Assert.False(attack.IsCompleted);

        r.Approach(AttackRig.Reedshark);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, attack.Outcome!.Value.Kind);
        Assert.Contains("approach", attack.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(AttackSequenceState.Idle, r.Seq.State); // the run's start is the sequence's, later
    }

    [Fact]
    public void A_turn_toward_the_target_confirms_too()
    {
        var r = Start(out var attack);

        r.Approach(AttackRig.Reedshark, MovementType.TurnToObject);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, attack.Outcome!.Value.Kind);
    }

    [Fact]
    public void The_first_swing_confirms_when_the_target_was_already_in_reach()
    {
        var r = Start(out var attack);

        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, attack.Outcome!.Value.Kind);
        Assert.Contains("sequence started", attack.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(1, r.Seq.Swings);
    }

    [Fact]
    public void The_confirmation_does_not_wait_for_the_run_to_end_and_the_sequence_goes_on()
    {
        var r = Start(out var attack);
        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Tick();
        Assert.True(attack.IsCompleted);

        r.Done(0);
        r.Commence();
        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Done(AttackRig.Over);

        Assert.Equal(AttackSequenceState.Done, r.Seq.State);
        Assert.Equal(2, r.Seq.Swings);
    }

    [Fact]
    public void A_hit_or_a_miss_naming_the_target_confirms_and_one_naming_another_does_not()
    {
        var hit = Start(out var a);
        hit.Hit("Drudge");
        hit.Tick();
        Assert.False(a.IsCompleted);
        hit.Hit("Adult Reedshark");
        hit.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, a.Outcome!.Value.Kind);

        var miss = Start(out var b);
        miss.Evade("Adult Reedshark");
        miss.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, b.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_squelched_01B1_leaves_the_kill_notice_to_confirm_the_attack()
    {
        // capture.md C1: the kill is a 01AD with no 01B1. The 01B1 and 01B3 can be squelched (W/Player_Combat.cs 150, 162), so the 01AD is the sign that cannot be.
        var r = Start(out var attack);

        r.Kill("You have killed Something Else!");
        r.Tick();
        Assert.False(attack.IsCompleted);

        r.Kill("You have killed Adult Reedshark!");
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, attack.Outcome!.Value.Kind);
        Assert.Contains("0x01AD", attack.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x001Du)]
    [InlineData(0x04EBu)]
    [InlineData(0x04CCu)]
    public void A_refusing_WeenieError_is_a_ServerError_with_its_code_when_the_01A7_follows(uint code)
    {
        var r = Start(out var attack);

        r.W.WeenieError(code);
        r.Done(AttackRig.Over);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, attack.Outcome!.Value.Kind);
        Assert.Equal(code, attack.Outcome.Value.Code);
    }

    [Fact]
    public void A_refusal_whose_01A7_is_not_a_change_still_settles_at_the_end_of_the_tick()
    {
        // The sequence already ended 0x0036, so the refusal 0x01A7 {0x0036} changes nothing and publishes nothing (TODO(m4-attack-done-event)): the error settles it.
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        r.Done(AttackRig.Over);
        var attack = r.Api.Attack(AttackRig.Reedshark, AttackHeight.High, 1f, ActionSource.Automation);
        r.Tick();

        r.W.WeenieError(0x04EB);
        r.Done(AttackRig.Over);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, attack.Outcome!.Value.Kind);
        Assert.Equal(0x04EBu, attack.Outcome.Value.Code);
    }

    [Fact]
    public void A_028B_with_its_string_is_a_ServerError_and_keeps_the_text()
    {
        var r = Start(out var attack);

        r.Error028B(0x0498, "Adult Reedshark");
        r.Done(AttackRig.Over);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, attack.Outcome!.Value.Kind);
        Assert.Equal(0x0498u, attack.Outcome.Value.Code);
        Assert.Contains("Adult Reedshark", attack.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_you_cannot_attack_transient_string_is_a_ServerError_and_another_transient_line_is_not()
    {
        var other = Start(out var a);
        other.Transient("You have no ammunition!");
        other.Tick();
        Assert.False(a.IsCompleted);

        var r = Start(out var attack);
        r.Transient("You cannot attack that");
        r.Done(AttackRig.Over);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, attack.Outcome!.Value.Kind);
        Assert.Contains("cannot attack", attack.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrelated_028A_does_not_refuse_the_attack()
    {
        var r = Start(out var attack);

        r.W.WeenieError(0x0055);
        r.Tick();

        Assert.False(attack.IsCompleted);
    }

    [Fact]
    public void A_01A7_with_a_code_and_no_start_and_no_error_is_a_silent_refusal_with_the_code_kept()
    {
        var r = Start(out var attack);

        r.Done(0x0099);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, attack.Outcome!.Value.Kind);
        Assert.Equal(0x0099u, attack.Outcome.Value.Code);
        Assert.Contains("silent refusal", attack.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_01A7_that_changes_nothing_after_a_Done_is_a_silent_refusal_at_once_not_at_the_timeout()
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Done(0x0036); // the earlier sequence ended
        Assert.Equal(AttackSequenceState.Done, r.Seq.State);

        var attack = r.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.5f, ActionSource.Automation);
        r.Tick();
        r.Done(0x0036); // the refusal 01A7 is the same code in the same state: not a field change (TODO(m4-attack-done-event), closed)
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, attack.Outcome!.Value.Kind);
        Assert.Equal(0x0036u, attack.Outcome.Value.Code);
        Assert.Contains("silent refusal", attack.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_swing_seen_before_the_error_wins_the_confirmation()
    {
        var r = Start(out var attack);

        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.W.WeenieError(0x001D); // after the start: not a refusal of this attack
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, attack.Outcome!.Value.Kind);
    }

    [Fact]
    public void Silence_times_out_at_20_seconds_from_the_send()
    {
        var r = Start(out var attack);

        r.Tick(19.5);
        Assert.False(attack.IsCompleted);
        r.Tick(1);

        Assert.Equal(ActionOutcomeKind.TimedOut, attack.Outcome!.Value.Kind);
        Assert.Equal(20.0, ActionPolicy.AttackSeconds);
    }

    [Fact]
    public void An_attack_sent_while_a_sequence_is_running_is_sent_and_confirmed_at_once_as_queued()
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark); // a sequence is running (ACE queues the power: E/AttackQueue.cs 24-40, and answers nothing)
        Assert.True(r.Seq.Active);

        var attack = r.Api.Attack(AttackRig.Reedshark, AttackHeight.Low, 0.8f, ActionSource.Automation);
        r.Tick();

        Assert.Single(r.Net.Of(FakeSendKind.MeleeAttack));
        Assert.Equal(ActionOutcomeKind.Confirmed, attack.Outcome!.Value.Kind);
        Assert.Contains("queued", attack.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.True(r.Seq.Active);
    }

    [Fact]
    public void A_second_attack_waits_for_the_first_one_to_be_confirmed()
    {
        var r = Start(out var first);
        var second = r.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.5f, ActionSource.Automation);
        r.Tick();
        Assert.Single(r.Net.Of(FakeSendKind.MeleeAttack));
        Assert.False(second.IsCompleted);

        r.Approach(AttackRig.Reedshark);
        r.Tick();
        Assert.True(first.IsCompleted);
        Assert.Equal(2, r.Net.Of(FakeSendKind.MeleeAttack).Count);
    }

    [Fact]
    public void Not_in_the_world_the_attack_is_refused_and_nothing_is_sent()
    {
        var w = new WorldActionRig(present: false);
        var attack = w.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.5f, ActionSource.Automation);
        w.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, attack.Outcome!.Value.Kind);
        Assert.Empty(w.H.Net.Of(FakeSendKind.MeleeAttack));
    }
}
