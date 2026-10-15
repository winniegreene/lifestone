using Lifestone.Client.Play;
using Lifestone.Client.Streaming;

namespace Lifestone.Client.Tests.Play;

/// <summary>The world adapter's pure rules (docs/portal-space.md §11.4): every piece gets a settled answer or a pending one, never a stuck one.</summary>
public sealed class AttachStateMappingTests
{
    [Theory]
    [InlineData(null, AttachState.Pending)]
    [InlineData(LandblockLoadState.Loading, AttachState.Pending)]
    [InlineData(LandblockLoadState.Loaded, AttachState.Attached)]
    [InlineData(LandblockLoadState.Failed, AttachState.Failed)]
    public void Terrain_follows_the_planners_state(LandblockLoadState? planner, AttachState expected)
    {
        Assert.Equal(expected, AttachStateMapping.Terrain(planner));
    }

    [Theory]
    [InlineData(true, AttachState.Pending, AttachState.Pending)]
    [InlineData(true, AttachState.Attached, AttachState.Attached)]
    [InlineData(true, AttachState.Failed, AttachState.Failed)]
    [InlineData(true, AttachState.Disabled, AttachState.Disabled)]
    [InlineData(false, AttachState.Pending, AttachState.Disabled)]
    [InlineData(false, AttachState.Attached, AttachState.Disabled)]
    public void Objects_are_the_systems_answer_unless_the_layer_is_off(bool running, AttachState system, AttachState expected)
    {
        Assert.Equal(expected, AttachStateMapping.Objects(running, system));
    }

    [Fact]
    public void Collision_is_disabled_until_the_physics_streamer_is_wired()
    {
        Assert.Equal(AttachState.Disabled, AttachStateMapping.Collision());
        Assert.True(AttachStateMapping.Collision().IsSettled());
    }

    [Fact]
    public void A_cell_without_the_interiors_layer_is_disabled_whatever_else_holds()
    {
        Assert.Equal(AttachState.Disabled, AttachStateMapping.Cell(false, true, true, true, LandblockLoadState.Loaded));
        Assert.Equal(AttachState.Disabled, AttachStateMapping.Cell(false, false, false, false, null));
    }

    [Fact]
    public void An_attached_cell_is_attached()
    {
        Assert.Equal(AttachState.Attached, AttachStateMapping.Cell(true, true, true, true, LandblockLoadState.Loaded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(LandblockLoadState.Loading)]
    public void A_cell_whose_landblock_has_not_registered_yet_is_pending(LandblockLoadState? landblock)
    {
        Assert.Equal(AttachState.Pending, AttachStateMapping.Cell(true, false, false, false, landblock));
    }

    [Fact]
    public void A_registered_landblock_that_has_not_meshed_an_existing_cell_yet_is_pending()
    {
        // SetPlayerCell / PlaceViewer ask the layer to mesh the rest of the landblock; until then the cell is on its way.
        Assert.Equal(AttachState.Pending, AttachStateMapping.Cell(true, false, true, true, LandblockLoadState.Loaded));
    }

    [Fact]
    public void A_cell_the_registered_landblock_does_not_contain_is_failed_not_pending_for_ever()
    {
        var state = AttachStateMapping.Cell(true, false, true, false, LandblockLoadState.Loaded);
        Assert.Equal(AttachState.Failed, state);
        Assert.True(state.IsSettled());
    }

    [Fact]
    public void A_cell_in_a_landblock_that_failed_to_load_is_failed()
    {
        Assert.Equal(AttachState.Failed, AttachStateMapping.Cell(true, false, false, false, LandblockLoadState.Failed));
    }

    [Theory]
    [InlineData(false, false, false, null, true)] // no interiors layer: nothing to wait for
    [InlineData(true, false, false, LandblockLoadState.Failed, true)] // the landblock failed
    [InlineData(true, true, false, LandblockLoadState.Loaded, true)] // registered, but the cell is not in it
    [InlineData(true, true, true, LandblockLoadState.Loaded, false)] // known: the list is read, not settled by default
    [InlineData(true, false, false, LandblockLoadState.Loading, false)] // still loading: the list may yet become known
    [InlineData(true, false, false, null, false)]
    public void The_visible_cell_list_is_settled_only_when_it_can_never_become_known(
        bool interiors, bool registered, bool exists, LandblockLoadState? landblock, bool expected)
    {
        Assert.Equal(expected, AttachStateMapping.VisibleListSettled(interiors, registered, exists, landblock));
    }
}
