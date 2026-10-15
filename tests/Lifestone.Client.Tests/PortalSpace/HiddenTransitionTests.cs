using DatReaderWriter.Enums;
using Lifestone.Client.PortalSpace;
using Lifestone.Data.Effects;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>Step PS11: retail's hide and unhide rule (<c>CPhysicsObj::set_state</c>, <c>set_hidden</c>; m2-log-in.md §4.9).</summary>
public sealed class HiddenTransitionTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Theory]
    [InlineData(0x00000000u, 0x00004000u, HiddenChange.Hide)]
    [InlineData(0x00400C08u, 0x00404C08u, HiddenChange.Hide)]
    [InlineData(0x00004000u, 0x00000000u, HiddenChange.Unhide)]
    [InlineData(0x00404C18u, 0x00400C08u, HiddenChange.Unhide)]
    [InlineData(0x00004000u, 0x00004008u, HiddenChange.None)] // Hidden unchanged
    [InlineData(0x00000010u, 0x00000020u, HiddenChange.None)]
    [InlineData(0x00000000u, 0x00000000u, HiddenChange.None)]
    public void Only_a_change_of_the_hidden_bit_acts(uint oldState, uint newState, HiddenChange change)
    {
        Assert.Equal(change, HiddenTransition.Classify(oldState, newState));
    }

    [Fact]
    public void An_object_created_hidden_plays_the_hidden_script_from_the_constructor_state()
    {
        Assert.Equal(0x00400C08u, HiddenTransition.ConstructedState);
        Assert.Equal(0u, HiddenTransition.ConstructedState & HiddenTransition.Hidden);

        Assert.Equal(HiddenChange.Hide, HiddenTransition.ClassifyCreation(0x00404C08)); // the logging-in player
        Assert.Equal(HiddenChange.None, HiddenTransition.ClassifyCreation(0x00400C08));
        Assert.Equal(HiddenChange.None, HiddenTransition.ClassifyCreation(0x00000001)); // a static, shown
    }

    [Fact]
    public void Each_change_plays_its_script_type()
    {
        Assert.Equal(PlayScript.Hidden, HiddenTransition.Script(HiddenChange.Hide));
        Assert.Equal(PlayScript.UnHide, HiddenTransition.Script(HiddenChange.Unhide));
        Assert.Equal(PlayScript.Invalid, HiddenTransition.Script(HiddenChange.None));
        Assert.Equal(0x76u, (uint)PlayScript.Hidden);
        Assert.Equal(0x75u, (uint)PlayScript.UnHide);
    }

    [Theory]
    [InlineData(0x00404C08u, HiddenChange.Hide, 0x00404C10u)] // ReportCollisions cleared, IgnoreCollisions set
    [InlineData(0x00404C00u, HiddenChange.Hide, 0x00404C10u)] // ReportCollisions already clear
    [InlineData(0x00400C10u, HiddenChange.Unhide, 0x00400C08u)] // IgnoreCollisions cleared, ReportCollisions set
    [InlineData(0x00400C08u, HiddenChange.Unhide, 0x00400C08u)] // ReportCollisions already set
    [InlineData(0x00000025u, HiddenChange.None, 0x00000025u)]
    public void Set_hidden_changes_the_collision_bits(uint state, HiddenChange change, uint after)
    {
        Assert.Equal(after, HiddenTransition.ApplyToObjectState(state, change));
    }

    [Fact]
    public void Children_stop_drawing_while_hidden()
    {
        Assert.Equal(0x00400C28u, HiddenTransition.ApplyToChildState(0x00400C08, HiddenChange.Hide));
        Assert.Equal(0x00400C08u, HiddenTransition.ApplyToChildState(0x00400C28, HiddenChange.Unhide));
        Assert.Equal(0x00400C28u, HiddenTransition.ApplyToChildState(0x00400C28, HiddenChange.None));
    }

    [RequiresDatsFact]
    public void On_the_real_dats_script_table_0x34000004_plays_the_hidden_and_unhide_scripts()
    {
        var effects = new EffectObject(new DatEffectSource(fixture.Dats), new RetailRandom(1), physicsScriptTableId: 0x34000004);

        Assert.Equal(0x33000331u, HiddenTransition.Play(effects, HiddenTransition.ClassifyCreation(0x00404C08)));
        Assert.Equal(0x3300032Fu, HiddenTransition.Play(effects, HiddenChange.Unhide));
        Assert.Equal(0u, HiddenTransition.Play(effects, HiddenChange.None));
    }

    [RequiresDatsFact]
    public void An_object_without_a_script_table_plays_nothing()
    {
        var effects = new EffectObject(new DatEffectSource(fixture.Dats), new RetailRandom(1));

        Assert.Equal(0u, HiddenTransition.Play(effects, HiddenChange.Hide));
        Assert.Equal(0u, HiddenTransition.Play(effects, HiddenChange.Unhide));
    }
}
