using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Physics;

/// <summary>Retail <c>Frame::set_heading</c> and <c>set_vector_heading</c>: a frame turned to a compass heading faces it.</summary>
public sealed class RetailHeadingsTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(20f)]
    [InlineData(90f)]
    [InlineData(200f)]
    [InlineData(359f)]
    public void A_frame_turned_to_a_heading_faces_it(float degrees)
    {
        var frame = RetailHeadings.SetHeading(AcFrame.Identity, degrees);
        var heading = new CellFrame(0x40400001, frame).HeadingDegrees;
        var difference = Math.Abs(((heading - degrees + 540.0) % 360.0) - 180.0);
        Assert.True(difference < 1e-3, $"heading {heading} for {degrees}");
        Assert.Equal(0f, RetailHeadings.GetVectorHeading(frame).Z, 5);
    }

    [Fact]
    public void A_heading_vector_east_of_north_turns_the_forward_axis_to_it()
    {
        var frame = RetailHeadings.SetVectorHeading(AcFrame.Identity, new Vector3(1f, 1f, 0f));
        var forward = RetailHeadings.GetVectorHeading(frame);
        Assert.Equal(MathF.Sqrt(0.5f), forward.X, 5);
        Assert.Equal(MathF.Sqrt(0.5f), forward.Y, 5);
    }
}
