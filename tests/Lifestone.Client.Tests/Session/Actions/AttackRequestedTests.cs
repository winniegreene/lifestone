using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="AttackRequested"/> (M4 1.3 s1): the Action API publishes it once per attack that reaches the wire, from any source, and never for an attack refused before the send, a swing, a 0x01A7 or a cancel. The power bar
/// and the combat window's meter begin from it.
/// </summary>
public sealed class AttackRequestedTests
{
    private static List<AttackRequested> Collect(AttackRig r)
    {
        var seen = new List<AttackRequested>();
        r.W.H.R.Bus.Subscribe<AttackRequested>((in AttackRequested e) => seen.Add(e));
        return seen;
    }

    [Fact]
    public void A_melee_send_publishes_once_with_the_target_height_power_source_and_frame_time()
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        var seen = Collect(r);

        r.Api.Attack(AttackRig.Reedshark, AttackHeight.High, 0.75f, ActionSource.Ui);
        Assert.Empty(seen); // queued: not sent yet
        r.Tick(0.5);

        var sent = Assert.Single(seen);
        Assert.Equal(new AttackRequested(AttackRig.Reedshark, AttackHeight.High, 0.75f, false, ActionSource.Ui, sent.Time), sent);
        Assert.Equal(r.Api.FrameTime, sent.Time);
        Assert.Single(r.Net.Of(FakeSendKind.MeleeAttack));

        r.Tick(1.0); // later frames publish nothing
        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Done(0);
        r.Commence();
        r.Done(AttackRig.Over);
        r.Tick(1.0);
        Assert.Single(seen);
    }

    [Fact]
    public void A_missile_send_publishes_with_the_missile_flag()
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        var seen = Collect(r);
        r.Api.MissileAttack(AttackRig.Reedshark, AttackHeight.Low, 0.4f, ActionSource.Host);
        r.Tick();
        var sent = Assert.Single(seen);
        Assert.True(sent.Missile);
        Assert.Equal(AttackHeight.Low, sent.Height);
        Assert.Equal(0.4f, sent.Power);
        Assert.Equal(ActionSource.Host, sent.Source);
    }

    [Fact]
    public void An_attack_refused_before_the_send_publishes_nothing()
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        var seen = Collect(r);
        r.Api.Attack(0, AttackHeight.Medium, 0.5f, ActionSource.Ui);
        r.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, float.NaN, ActionSource.Ui);
        r.Api.MissileAttack(AttackRig.Reedshark, (AttackHeight)0, 0.5f, ActionSource.Ui);
        r.Api.CancelAttack(ActionSource.Host);
        r.Tick(1.0);
        Assert.Empty(seen);
        Assert.Empty(r.Net.Of(FakeSendKind.MeleeAttack));
    }

    [Fact]
    public void An_attack_made_while_a_run_is_going_is_a_send_and_publishes_again()
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        var seen = Collect(r);
        r.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.5f, ActionSource.Host);
        r.Tick();
        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Api.Attack(AttackRig.Reedshark, AttackHeight.Low, 0.9f, ActionSource.Ui); // ACE queues the power; the client still sent it
        r.Tick();
        Assert.Equal(2, seen.Count);
        Assert.Equal(0.9f, seen[1].Power);
        Assert.Equal(2, r.Net.Of(FakeSendKind.MeleeAttack).Count);
    }
}
