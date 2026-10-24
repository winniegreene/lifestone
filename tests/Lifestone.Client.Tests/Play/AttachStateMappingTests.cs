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
    [InlineData(true, AttachState.Attached, AttachState.Attached)]
    [InlineData(true, AttachState.Failed, AttachState.Failed)]
    [InlineData(false, AttachState.Pending, AttachState.Disabled)]
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

    [Theory]
    [InlineData(false, true, true, true, LandblockLoadState.Loaded, AttachState.Disabled)] // no interiors layer: disabled whatever else holds
    [InlineData(false, false, false, false, null, AttachState.Disabled)]
    [InlineData(true, true, true, true, LandblockLoadState.Loaded, AttachState.Attached)]
    [InlineData(true, false, false, false, null, AttachState.Pending)] // the landblock has not registered yet
    [InlineData(true, false, false, false, LandblockLoadState.Loading, AttachState.Pending)]
    [InlineData(true, false, true, true, LandblockLoadState.Loaded, AttachState.Pending)] // SetPlayerCell / PlaceViewer ask the layer to mesh the rest of the landblock; until then the cell is on its way
    [InlineData(true, false, true, false, LandblockLoadState.Loaded, AttachState.Failed)] // a cell the registered landblock does not contain: failed, not pending for ever
    [InlineData(true, false, false, false, LandblockLoadState.Failed, AttachState.Failed)] // a landblock that failed to load
    public void The_cell_state_follows_the_layer_the_registration_the_mesh_and_the_landblock(bool interiors, bool attached, bool registered, bool cellExists, LandblockLoadState? landblock, AttachState expected)
    {
        var state = AttachStateMapping.Cell(interiors, attached, registered, cellExists, landblock);
        Assert.Equal(expected, state);
        if (expected == AttachState.Failed)
        {
            Assert.True(state.IsSettled());
        }
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
