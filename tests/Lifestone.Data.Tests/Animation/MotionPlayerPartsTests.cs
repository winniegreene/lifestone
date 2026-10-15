using Lifestone.Data.Animation;
using Lifestone.Data.Tests.Motion;

namespace Lifestone.Data.Tests.Animation;

/// <summary>
/// The part-array operations of <see cref="MotionPlayer"/> the motion interpreter uses (retail <c>CPartArray</c>):
/// motions without the completion check, the pending count, and <see cref="MotionPlayer.RemoveLinkAnimations"/>.
/// </summary>
public sealed class MotionPlayerPartsTests
{
    [Fact]
    public void A_new_player_has_completed_its_default_motion()
    {
        var player = Player(out _);
        Assert.Equal(0, player.PendingAnimationCount);
        Assert.Empty(player.Manager.Pending);
    }

    [Fact]
    public void Interpreted_motions_wait_for_the_completion_check()
    {
        var player = Player(out var done);

        Assert.True(player.DoInterpretedMotion(MotionCommands.NonCombat, 1f)); // the current style: no animations
        Assert.Equal(1, player.PendingAnimationCount);
        Assert.Empty(done);

        player.CheckForCompletedMotions();
        Assert.Equal(0, player.PendingAnimationCount);
        Assert.Equal([(MotionCommands.NonCombat, true)], done);
    }

    [Fact]
    public void Removing_link_animations_drops_the_links_and_fails_every_pending_motion()
    {
        var player = Player(out var done);
        Assert.True(player.DoInterpretedMotion(MotionCommands.WalkForward, 1f));
        Assert.Equal(new[] { SyntheticMotionTable.ReadyToWalk, SyntheticMotionTable.Walk }, player.Sequence.Nodes.Select(n => n.AnimId).ToArray());
        Assert.Equal(1, player.PendingAnimationCount);

        player.RemoveLinkAnimations();

        Assert.Equal(new[] { SyntheticMotionTable.Walk }, player.Sequence.Nodes.Select(n => n.AnimId).ToArray());
        Assert.Equal([(MotionCommands.WalkForward, false)], done);
        Assert.Equal(0, player.PendingAnimationCount);
        Assert.Empty(player.Manager.Pending);
    }

    [Fact]
    public void A_refused_motion_queues_nothing()
    {
        var player = Player(out _);
        Assert.False(player.StopInterpretedMotion(MotionCommands.SideStepRight, 1f));
        Assert.False(player.DoInterpretedMotion(MotionCommands.Dead, 1f)); // no Dead cycle in the table
        Assert.Equal(0, player.PendingAnimationCount);

        player.StopCompletelyInternal(); // always queues
        Assert.Equal(1, player.PendingAnimationCount);
        player.InitializeMotionTables();
        Assert.Equal(2, player.PendingAnimationCount);
        Assert.Equal(2, player.Manager.Pending.Count());
    }

    private static MotionPlayer Player(out List<(uint, bool)> done)
    {
        var (table, animations) = SyntheticMotionTable.Create();
        var player = new MotionPlayer(table, animations);
        var completed = new List<(uint, bool)>();
        player.Manager.MotionDone += (motion, success) => completed.Add((motion, success));
        done = completed;
        return player;
    }
}
