using Lifestone.Data.Physics;

namespace Lifestone.Data.Tests.Physics;

/// <summary>The decision table's switches (docs/physics.md §2): retail by default, ACE's value for every row in <see cref="PhysicsCompat.Ace"/>.</summary>
public sealed class PhysicsCompatTests
{
    [Fact]
    public void Retail_is_the_default_and_holds_the_retail_values()
    {
        var retail = PhysicsCompat.Retail;
        Assert.Equal(new PhysicsCompat(), retail);
        Assert.True(retail.IsRetail);
        Assert.Equal(0.2, retail.MaxQuantum);
        Assert.False(retail.CheckWalkableOverwritesPathBackup);
        Assert.Null(retail.MaxTransitionSteps);
        Assert.False(retail.ConstraintOffsetAssigned);
        Assert.False(retail.InterpolationOffsetDiscarded);
        Assert.False(retail.AddCellBlockSameLandblockOnly);
        Assert.Equal(1.0, retail.PositionEventInterval);
    }

    [Fact]
    public void Ace_changes_every_row_to_ACEs_value()
    {
        var ace = PhysicsCompat.Ace;
        Assert.False(ace.IsRetail);
        Assert.Equal(0.1, ace.MaxQuantum);
        Assert.True(ace.CheckWalkableOverwritesPathBackup);
        Assert.Equal(1000, ace.MaxTransitionSteps);
        Assert.True(ace.ConstraintOffsetAssigned);
        Assert.True(ace.InterpolationOffsetDiscarded);
        Assert.True(ace.AddCellBlockSameLandblockOnly);
        Assert.Equal(1.875, ace.PositionEventInterval);
    }

    [Fact]
    public void One_switch_can_be_changed_on_its_own()
    {
        var oneRow = PhysicsCompat.Retail with { PositionEventInterval = 1.875 };
        Assert.False(oneRow.IsRetail);
        Assert.Equal(PhysicsCompat.Retail.MaxQuantum, oneRow.MaxQuantum);
        Assert.Equal(PhysicsCompat.Retail, oneRow with { PositionEventInterval = 1.0 });
    }
}
