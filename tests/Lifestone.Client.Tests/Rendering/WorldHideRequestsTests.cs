using Lifestone.Client.Rendering;

namespace Lifestone.Client.Tests.Rendering;

/// <summary>The reference counts behind the world arbiter (<c>WorldVisibility</c>, the sole writer of <c>Disable3D</c>).</summary>
public sealed class WorldHideRequestsTests
{
    [Fact]
    public void The_world_is_hidden_while_any_request_is_held()
    {
        var requests = new WorldHideRequests();
        Assert.False(requests.Hidden);
        Assert.True(requests.Request("portal-space")); // the world just became hidden
        Assert.False(requests.Request("retail-ui"));
        Assert.False(requests.Release("portal-space")); // still held by the UI
        Assert.True(requests.Hidden);
        Assert.True(requests.Release("retail-ui")); // the world just became visible
        Assert.False(requests.Hidden);
        Assert.Equal(0, requests.Total);
        Assert.Equal("none", requests.Describe());
    }

    [Fact]
    public void One_owner_may_hold_several_requests_and_must_release_each()
    {
        var requests = new WorldHideRequests();
        requests.Request("a");
        requests.Request("a");
        Assert.Equal(2, requests.Held("a"));
        Assert.False(requests.Release("a"));
        Assert.True(requests.Hidden);
        Assert.True(requests.Release("a"));
        Assert.Equal(0, requests.Unbalanced);
    }

    [Fact]
    public void Set_holds_exactly_one_request_and_reports_changes_of_visibility()
    {
        var requests = new WorldHideRequests();
        Assert.True(requests.Set("portal-space", hidden: true));
        Assert.False(requests.Set("portal-space", hidden: true)); // idempotent
        Assert.Equal(1, requests.Held("portal-space"));
        Assert.Equal(1, requests.Total);
        Assert.False(requests.Set("retail-ui", hidden: true)); // already hidden by the other
        Assert.False(requests.Set("portal-space", hidden: false));
        Assert.True(requests.Hidden); // the UI still covers the world
        Assert.True(requests.Set("retail-ui", hidden: false));
        Assert.False(requests.Hidden);

        // Set replaces whatever the owner held.
        requests.Request("x");
        requests.Request("x");
        requests.Set("x", hidden: true);
        Assert.Equal(1, requests.Held("x"));
        Assert.Equal(1, requests.Total);
    }

    [Fact]
    public void A_release_without_a_request_is_ignored_and_counted()
    {
        var requests = new WorldHideRequests();
        Assert.False(requests.Release("nobody"));
        requests.Request("a");
        Assert.False(requests.Release("b")); // b holds nothing, even though the world is hidden
        Assert.True(requests.Hidden);
        Assert.Equal(2, requests.Unbalanced);
        Assert.Equal("a(1)", requests.Describe());
        Assert.Throws<ArgumentException>(() => requests.Request(string.Empty));
    }
}
