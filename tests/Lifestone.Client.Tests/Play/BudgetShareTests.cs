using Lifestone.Client.Play;

namespace Lifestone.Client.Tests.Play;

/// <summary>The frame-budget table (docs/game-core.md §1.2): shares come off the queue's budget down to a 1 ms floor.</summary>
public sealed class BudgetShareTests
{
    [Fact]
    public void Without_shares_the_queue_keeps_its_configured_budget()
    {
        var share = new BudgetShare(4);
        Assert.Equal(4.0, share.EffectiveMilliseconds);
        Assert.Equal(TimeSpan.FromMilliseconds(4), share.Effective);
        Assert.False(share.IsReduced);
        Assert.Empty(share.Shares);
        Assert.Equal("queue 4.0 ms of 4.0", share.Describe());
    }

    [Fact]
    public void Shares_are_subtracted_from_the_budget()
    {
        var share = new BudgetShare(4);
        share.Set("tunnel", 1);
        Assert.Equal(3.0, share.EffectiveMilliseconds);
        Assert.True(share.IsReduced);
        share.Set("session", 0.5);
        Assert.Equal(2.5, share.EffectiveMilliseconds);
        Assert.Equal(1.5, share.SharesMilliseconds);
        Assert.Equal("queue 2.5 ms of 4.0 (tunnel 1.0, session 0.5)", share.Describe());
    }

    [Fact]
    public void The_budget_never_falls_below_the_one_millisecond_floor()
    {
        var share = new BudgetShare(4);
        share.Set("tunnel", 1);
        share.Set("session", 2);
        Assert.Equal(1.0, share.EffectiveMilliseconds);
        share.Set("other", 10);
        Assert.Equal(BudgetShare.FloorMilliseconds, share.EffectiveMilliseconds);
    }

    [Fact]
    public void A_budget_the_owner_set_below_the_floor_is_left_alone()
    {
        var share = new BudgetShare(0.5);
        Assert.Equal(0.5, share.EffectiveMilliseconds);
        share.Set("tunnel", 1);
        Assert.Equal(0.5, share.EffectiveMilliseconds);
        Assert.False(share.IsReduced);
    }

    [Fact]
    public void Releasing_a_share_gives_the_time_back()
    {
        var share = new BudgetShare(4);
        share.Set("tunnel", 1);
        share.Set("session", 2);
        share.Set("tunnel", 0);
        Assert.Equal(2.0, share.EffectiveMilliseconds);
        Assert.Equal(0.0, share.Get("tunnel"));
        Assert.Equal(2.0, share.Get("session"));
        share.Set("session", 0);
        Assert.Equal(4.0, share.EffectiveMilliseconds);
        Assert.Empty(share.Shares);
    }

    [Fact]
    public void Setting_a_name_again_replaces_its_amount_and_keeps_its_place()
    {
        var share = new BudgetShare(8);
        share.Set("tunnel", 1);
        share.Set("session", 2);
        share.Set("tunnel", 3);
        Assert.Equal(2, share.Shares.Count);
        Assert.Equal(("tunnel", 3.0), share.Shares[0]);
        Assert.Equal(3.0, share.EffectiveMilliseconds);
        share.Set("never-taken", 0);
        Assert.Equal(2, share.Shares.Count);
    }

    [Fact]
    public void A_new_base_budget_applies_at_once()
    {
        var share = new BudgetShare(4);
        share.Set("tunnel", 1);
        share.BaseMilliseconds = 8;
        Assert.Equal(7.0, share.EffectiveMilliseconds);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_bad_budget_or_share_is_refused(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetShare(value));
        var share = new BudgetShare(4);
        Assert.Throws<ArgumentOutOfRangeException>(() => share.Set("tunnel", value));
        Assert.Throws<ArgumentOutOfRangeException>(() => share.BaseMilliseconds = value);
        Assert.Empty(share.Shares);
        Assert.Equal(4.0, share.BaseMilliseconds);
    }

    [Fact]
    public void A_share_needs_a_name()
    {
        var share = new BudgetShare(4);
        Assert.Throws<ArgumentException>(() => share.Set("", 1));
        Assert.Throws<ArgumentNullException>(() => share.Set(null!, 1));
        Assert.Throws<ArgumentException>(() => share.Get(""));
    }
}
