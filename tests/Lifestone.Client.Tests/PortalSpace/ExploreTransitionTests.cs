using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>Step PS11: offline explore mode's portal transition (m2-log-in.md §4.11).</summary>
public sealed class ExploreTransitionTests
{
    [Fact]
    public void Enabled_it_stands_a_local_player_in_before_the_jump()
    {
        var progress = new TeleportProgress();
        var explore = new ExploreTransition(progress);
        var inProgressDuringJump = false;

        var started = explore.Jump(() => inProgressDuringJump = progress.InProgress && progress.IsLocal);

        Assert.True(started);
        Assert.True(inProgressDuringJump);
        Assert.True(progress.InProgress);
        Assert.Equal(1, explore.Transitions);
    }

    [Fact]
    public void Disabled_the_jump_runs_bare()
    {
        var progress = new TeleportProgress();
        var explore = new ExploreTransition(progress) { Enabled = false };
        var jumped = 0;

        Assert.False(explore.Jump(() => jumped++));

        Assert.Equal(1, jumped);
        Assert.False(progress.HasPlayer);
        Assert.False(progress.InProgress);
        Assert.Equal(0, explore.Transitions);
    }

    [Fact]
    public void A_second_jump_restarts_the_update_of_the_local_player()
    {
        var progress = new TeleportProgress();
        var explore = new ExploreTransition(progress);
        explore.Jump(() => { });
        progress.Tick(notBlockingForCells: true);
        Assert.False(progress.InProgress);

        Assert.True(explore.Jump(() => { }));

        Assert.True(progress.InProgress);
        Assert.True(progress.IsLocal);
        Assert.Equal(2, explore.Transitions);
    }

    [Fact]
    public void With_a_server_player_the_session_owns_portal_space_and_the_jump_runs_bare()
    {
        var progress = new TeleportProgress();
        progress.OnPlayerCreated();
        progress.Tick(notBlockingForCells: true);
        var explore = new ExploreTransition(progress);

        Assert.False(explore.Jump(() => { }));

        Assert.False(progress.InProgress);
        Assert.False(progress.IsLocal);
    }

    [Fact]
    public void Begin_stands_the_player_in_and_leaves_the_camera_move_to_the_caller()
    {
        var progress = new TeleportProgress();
        var explore = new ExploreTransition(progress);

        Assert.True(explore.Begin());

        Assert.True(progress.InProgress);
        Assert.True(progress.IsLocal);
        Assert.Equal(1, explore.Transitions);
    }

    [Fact]
    public void Begin_does_nothing_when_disabled_or_when_a_server_player_exists()
    {
        var off = new TeleportProgress();
        Assert.False(new ExploreTransition(off) { Enabled = false }.Begin());
        Assert.False(off.HasPlayer);

        var live = new TeleportProgress();
        live.OnPlayerCreated();
        live.Tick(notBlockingForCells: true);
        var explore = new ExploreTransition(live);
        Assert.False(explore.Begin());
        Assert.False(live.InProgress);
        Assert.Equal(0, explore.Transitions);
    }

    [Fact]
    public void A_null_jump_throws_and_changes_nothing()
    {
        var progress = new TeleportProgress();

        Assert.Throws<ArgumentNullException>(() => new ExploreTransition(progress).Jump(null!));
        Assert.False(progress.HasPlayer);
        Assert.Throws<ArgumentNullException>(() => new ExploreTransition(null!));
    }
}
