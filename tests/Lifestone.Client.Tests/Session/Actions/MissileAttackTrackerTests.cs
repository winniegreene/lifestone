using System.Numerics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <c>MissileAttack(target, height, accuracy)</c> (GameAction 0x000A; M4 1.2 s2) over cache/m4/1-2/capture.md C3: the aim (own F74C), then the launch tick (0197 the ammunition stack, the arrow F745), one send giving a run of
/// shots. Confirmed on the first launch; the refusals are WeenieErrors 0x0550 and the like or a transient string (including "You are out of ammunition!").
/// </summary>
public sealed class MissileAttackTrackerTests
{
    private static AttackRig Start(out ActionHandle attack, float accuracy = 0.5f)
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        r.Mode(CombatModes.Missile);
        r.WieldArrows(50);
        attack = r.Api.MissileAttack(AttackRig.Reedshark, AttackHeight.Medium, accuracy, ActionSource.Automation);
        r.Tick();
        return r;
    }

    [Fact]
    public void The_send_is_0x000A_with_the_height_and_accuracy_and_waits_for_a_launch()
    {
        var r = Start(out var shot);

        var sent = Assert.Single(r.Net.Of(FakeSendKind.MissileAttack));
        Assert.Equal(AttackRig.Reedshark, sent.Guid);
        Assert.Equal(2u, sent.Amount);
        Assert.Equal(0.5f, sent.Extent);
        Assert.False(shot.IsCompleted);
        Assert.Equal(ActionKind.MissileAttack, shot.Kind);
    }

    [Fact]
    public void The_ammunition_stack_falling_in_0197_confirms_the_launch()
    {
        var r = Start(out var shot);

        r.Stack(49);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, shot.Outcome!.Value.Kind);
        Assert.Equal(1, r.Seq.Shots);
    }

    [Fact]
    public void The_aim_that_starts_the_sequence_confirms_it()
    {
        var r = Start(out var shot);

        r.OwnMotion(AttackRig.Shoot, AttackRig.Reedshark);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, shot.Outcome!.Value.Kind);
    }

    [Fact]
    public void The_arrow_s_F745_alone_confirms_by_the_projectile_rule_when_no_0197_comes()
    {
        // The last arrow may delete the stack with no 0197 (TODO(m4-last-arrow-launch)): the arrow ahead of the player along the aim is the sign.
        var r = Start(out var shot);

        r.W.Create(0x80000430, x: 10, y: 21); // 1 m in front of the player, flying +Y at 20 m/s toward the target
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, shot.Outcome!.Value.Kind);
        Assert.Contains("arrow 0x80000430", shot.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("TODO(m3-cast-projectile-attribution)", shot.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(0, r.Seq.Shots); // the sequence did not count it: TODO(m4-last-arrow-launch) stays open
        // M4 1.3 s2 (the 1.2 review's R11): the TODO is logged, once, when the F745 is taken as the launch with no 0197.
        Assert.Single(r.W.H.Log.Lines, l => l.Contains("TODO(m4-last-arrow-launch)", StringComparison.Ordinal));
    }

    [Fact]
    public void The_last_arrow_todo_is_not_logged_when_a_0197_counted_the_shot()
    {
        var r = Start(out var shot);

        r.OwnMotion(AttackRig.Shoot, AttackRig.Reedshark);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, shot.Outcome!.Value.Kind);
        r.W.Create(0x80000433, x: 10, y: 21);
        r.Tick();
        Assert.DoesNotContain(r.W.H.Log.Lines, l => l.Contains("TODO(m4-last-arrow-launch)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missile_object_that_does_not_come_from_the_player_along_the_aim_does_not_confirm()
    {
        var r = Start(out var shot);

        r.W.Create(0x80000431, x: 10, y: 60, velocity: new Vector3(0, -20, 0)); // far away, flying at the player
        r.W.Create(0x80000432, x: 10, y: 21, velocity: new Vector3(0, -20, 0)); // near, flying back toward the player
        r.W.Create(0x80000433, x: 10, y: 21, missile: false); // not a projectile
        r.Tick();

        Assert.False(shot.IsCompleted);
    }

    [Theory]
    [InlineData(0x0550u)]
    [InlineData(0x03F9u)]
    [InlineData(0x001Du)]
    [InlineData(0x04EBu)]
    [InlineData(0x04CCu)]
    public void A_refusing_WeenieError_is_a_ServerError_with_its_code(uint code)
    {
        var r = Start(out var shot);

        r.W.WeenieError(code);
        r.Done(AttackRig.Over);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, shot.Outcome!.Value.Kind);
        Assert.Equal(code, shot.Outcome.Value.Code);
    }

    [Fact]
    public void Out_of_range_0x0550_is_a_ServerError_even_when_the_01A7_is_not_a_change()
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark", y: 120);
        r.Mode(CombatModes.Missile);
        r.WieldArrows(50);
        r.Done(AttackRig.Over);
        var shot = r.Api.MissileAttack(AttackRig.Reedshark, AttackHeight.Low, 1f, ActionSource.Automation);
        r.Tick();

        r.W.WeenieError(0x0550);
        r.Done(AttackRig.Over);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, shot.Outcome!.Value.Kind);
        Assert.Equal(0x0550u, shot.Outcome.Value.Code);
    }

    [Fact]
    public void Out_of_ammunition_is_a_ServerError_from_the_transient_string()
    {
        var r = Start(out var shot);

        r.Transient("You are out of ammunition!");
        r.Done(AttackRig.Over);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, shot.Outcome!.Value.Kind);
        Assert.Contains("out of ammunition", shot.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void You_cannot_attack_is_a_ServerError_from_the_transient_string()
    {
        var r = Start(out var shot);

        r.Transient("You cannot attack Adult Reedshark");
        r.Done(AttackRig.Over);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, shot.Outcome!.Value.Kind);
        Assert.Contains("cannot attack", shot.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrelated_transient_line_does_not_refuse_and_is_logged_once_as_a_named_TODO()
    {
        var r = Start(out var shot);

        r.Transient("You are now in the Hunter's Rest.");
        r.Transient("Another unrelated line.");
        r.Tick();

        Assert.False(shot.IsCompleted); // still waiting for a launch
        Assert.Single(r.W.H.Log.Lines, l => l.Contains("TODO(m4-missile-transient-text)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_01A7_with_a_code_and_no_launch_and_no_error_is_a_silent_refusal()
    {
        var r = Start(out var shot);

        r.Done(0x0099);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, shot.Outcome!.Value.Kind);
        Assert.Equal(0x0099u, shot.Outcome.Value.Code);
        Assert.Contains("silent refusal", shot.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Silence_times_out_at_10_seconds()
    {
        var r = Start(out var shot);

        r.Tick(9.5);
        Assert.False(shot.IsCompleted);
        r.Tick(1);

        Assert.Equal(ActionOutcomeKind.TimedOut, shot.Outcome!.Value.Kind);
        Assert.Equal(10.0, ActionPolicy.MissileAttackSeconds);
    }

    [Fact]
    public void A_shot_sent_while_a_sequence_is_running_is_queued()
    {
        var r = Start(out var first);
        r.Stack(49);
        r.Tick();
        Assert.True(first.IsCompleted);

        var second = r.Api.MissileAttack(AttackRig.Reedshark, AttackHeight.Medium, 0.5f, ActionSource.Automation);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, second.Outcome!.Value.Kind);
        Assert.Contains("queued", second.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(2, r.Net.Of(FakeSendKind.MissileAttack).Count);
    }
}
